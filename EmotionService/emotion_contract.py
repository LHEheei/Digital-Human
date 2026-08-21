from __future__ import annotations

from dataclasses import asdict, dataclass, field
from time import time
from typing import Mapping, Optional

KNOWN_LABELS = {"joy", "angry", "sorrow", "surprised", "neutral"}
FACE_MAX_AGE_MS = 3_000
VOICE_MAX_AGE_MS = 6_000
FUSION_MIN_WEIGHT = 0.5


@dataclass
class EmotionScore:
    """One supported apparent-expression probability from a model."""

    label: str
    score: float

    def normalized(self) -> "EmotionScore | None":
        label = (self.label or "").strip().lower()
        if label not in KNOWN_LABELS:
            return None
        return EmotionScore(label, max(0.0, min(1.0, float(self.score or 0.0))))


@dataclass
class EmotionResult:
    label: str = "neutral"
    score: float = 0.0
    source: str = "unknown"
    error: str = ""
    capturedAtUnixMs: int = 0
    # Optional classifier distribution. It is intentionally separate from
    # label/score, which may have passed calibration and temporal stability
    # rules after the model produced this one-frame distribution.
    scores: list[EmotionScore] = field(default_factory=list)

    def normalized(self) -> "EmotionResult":
        label = (self.label or "").strip().lower()
        source = (self.source or "").strip().lower()
        return EmotionResult(
            label=label if label in KNOWN_LABELS else "neutral",
            score=max(0.0, min(1.0, float(self.score or 0.0))),
            source=source if source in {"face", "voice", "fusion"} else "unknown",
            error=(self.error or "").strip(),
            capturedAtUnixMs=max(0, int(self.capturedAtUnixMs or 0)),
            scores=_normalize_scores(self.scores),
        )

    def as_dict(self) -> dict:
        return asdict(self.normalized())

    @property
    def usable(self) -> bool:
        value = self.normalized()
        return not value.error and value.score > 0 and value.source != "unknown"


def now_unix_ms() -> int:
    return int(time() * 1000)


def _normalize_scores(raw_scores: object) -> list[EmotionScore]:
    """Keep a bounded, deterministic distribution without trusting callers."""
    if not isinstance(raw_scores, (list, tuple)):
        return []

    by_label: dict[str, float] = {}
    for item in raw_scores:
        if isinstance(item, EmotionScore):
            value = item
        elif isinstance(item, Mapping):
            value = EmotionScore(str(item.get("label", "")), item.get("score", 0.0))
        else:
            continue
        normalized = value.normalized()
        if normalized is not None:
            by_label[normalized.label] = max(by_label.get(normalized.label, 0.0), normalized.score)

    return [EmotionScore(label, by_label[label]) for label in sorted(by_label)]


def failure(source: str, error: str, captured_at: Optional[int] = None) -> EmotionResult:
    return EmotionResult(
        source=source,
        error=error,
        capturedAtUnixMs=now_unix_ms() if captured_at is None else captured_at,
    ).normalized()


def map_face_label(value: str) -> str:
    mapping = {
        "happy": "joy",
        "sad": "sorrow",
        "surprise": "surprised",
        "angry": "angry",
        "neutral": "neutral",
    }
    return mapping.get((value or "").strip().lower(), "neutral")


def map_voice_label(value: str) -> str:
    normalized = (value or "").strip().lower()
    if "/" in normalized:
        normalized = normalized.rsplit("/", 1)[-1]
    mapping = {
        "happy": "joy",
        "joy": "joy",
        "angry": "angry",
        "sad": "sorrow",
        "sorrow": "sorrow",
        "surprise": "surprised",
        "surprised": "surprised",
        "neutral": "neutral",
        "\u751f\u6c14": "angry",
        "\u5f00\u5fc3": "joy",
        "\u96be\u8fc7": "sorrow",
        "\u5403\u60ca": "surprised",
        "\u4e2d\u7acb": "neutral",
    }
    return mapping.get(normalized, "neutral")


def fusion(face: Optional[EmotionResult], voice: Optional[EmotionResult], now_ms: Optional[int] = None) -> EmotionResult:
    now = now_unix_ms() if now_ms is None else now_ms
    face_item = _fresh(face, now, FACE_MAX_AGE_MS)
    voice_item = _fresh(voice, now, VOICE_MAX_AGE_MS)
    if face_item is None and voice_item is None:
        return failure("fusion", "No fresh usable emotion result.", now)

    if face_item is None or voice_item is None:
        only = face_item or voice_item
        if only.score < FUSION_MIN_WEIGHT:
            return failure("fusion", "Fusion confidence is below threshold.", only.capturedAtUnixMs)
        return EmotionResult(only.label, min(0.75, only.score), "fusion", "", only.capturedAtUnixMs).normalized()

    latest_capture = max(face_item.capturedAtUnixMs, voice_item.capturedAtUnixMs)
    if face_item.label == voice_item.label:
        combined = min(1.0, face_item.score * 0.55 + voice_item.score * 0.45)
        return EmotionResult(face_item.label, combined, "fusion", "", latest_capture).normalized()

    # Conflicting modalities are intentionally not resolved by whichever score
    # happens to be larger. They become a visible low-confidence neutral cue.
    return EmotionResult("neutral", min(0.49, max(face_item.score, voice_item.score) * 0.49), "fusion", "", latest_capture).normalized()


def _fresh(result: Optional[EmotionResult], now_ms: int, max_age_ms: int) -> Optional[EmotionResult]:
    if result is None:
        return None
    item = result.normalized()
    if not item.usable or item.capturedAtUnixMs <= 0 or now_ms < item.capturedAtUnixMs or now_ms - item.capturedAtUnixMs > max_age_ms:
        return None
    return item
