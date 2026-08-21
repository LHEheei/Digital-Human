from __future__ import annotations

from collections import Counter, deque
from dataclasses import dataclass, field
from statistics import median
from time import monotonic
from typing import Deque, Mapping

from emotion_contract import EmotionResult, now_unix_ms


PASSIVE_BASELINE_SAMPLE_COUNT = 8
CALIBRATION_MAX_SPREAD = 0.12
STABILITY_WINDOW_SIZE = 5
STABLE_MATCH_COUNT = 4
NO_FACE_RESET_COUNT = 5
SESSION_TTL_SECONDS = 300.0
MAX_SESSIONS = 32


def _mean(*values: float) -> float:
    usable = [value for value in values if value is not None]
    return sum(usable) / len(usable) if usable else 0.0


def _pair(values: Mapping[str, float], left: str, right: str) -> float:
    return _mean(values.get(left, 0.0), values.get(right, 0.0))


def _delta(current: Mapping[str, float], baseline: Mapping[str, float], *names: str) -> float:
    return max(0.0, max((current.get(name, 0.0) - baseline.get(name, 0.0) for name in names), default=0.0))


@dataclass(frozen=True)
class ExpressionCandidate:
    label: str
    score: float


def _is_stable_calibration(samples: list[Mapping[str, float]]) -> bool:
    """Reject a calibration sequence that contains a visible expression change.

    A per-device baseline is useful only when it represents a relaxed face.  We
    deliberately inspect the spread of the expression-relevant blendshapes
    rather than accepting any eight frames that happened to arrive first.
    """
    if len(samples) < PASSIVE_BASELINE_SAMPLE_COUNT:
        return False

    names = (
        "mouthSmileLeft", "mouthSmileRight", "browDownLeft", "browDownRight",
        "mouthFrownLeft", "mouthFrownRight", "mouthPressLeft", "mouthPressRight",
        "eyeWideLeft", "eyeWideRight", "browOuterUpLeft", "browOuterUpRight",
        "browInnerUp", "jawOpen",
    )
    return all(
        max(sample.get(name, 0.0) for sample in samples) - min(sample.get(name, 0.0) for sample in samples)
        <= CALIBRATION_MAX_SPREAD
        for name in names
    )


class FaceExpressionClassifier:
    """Conservative facial-expression cue rules over relative blendshape changes.

    The rules intentionally require paired visible signals for negative expressions.
    This avoids presenting a single eyebrow or lighting artifact as a confident mood.
    """

    @staticmethod
    def classify(current: Mapping[str, float], baseline: Mapping[str, float]) -> ExpressionCandidate:
        smile = _pair(
            {name: _delta(current, baseline, name) for name in ("mouthSmileLeft", "mouthSmileRight")},
            "mouthSmileLeft",
            "mouthSmileRight",
        )
        brow_down = _pair(
            {name: _delta(current, baseline, name) for name in ("browDownLeft", "browDownRight")},
            "browDownLeft",
            "browDownRight",
        )
        mouth_frown = _pair(
            {name: _delta(current, baseline, name) for name in ("mouthFrownLeft", "mouthFrownRight")},
            "mouthFrownLeft",
            "mouthFrownRight",
        )
        mouth_press = _pair(
            {name: _delta(current, baseline, name) for name in ("mouthPressLeft", "mouthPressRight")},
            "mouthPressLeft",
            "mouthPressRight",
        )
        eye_wide = _pair(
            {name: _delta(current, baseline, name) for name in ("eyeWideLeft", "eyeWideRight")},
            "eyeWideLeft",
            "eyeWideRight",
        )
        brow_outer_up = _pair(
            {name: _delta(current, baseline, name) for name in ("browOuterUpLeft", "browOuterUpRight")},
            "browOuterUpLeft",
            "browOuterUpRight",
        )
        brow_inner_up = _delta(current, baseline, "browInnerUp")
        jaw_open = _delta(current, baseline, "jawOpen")

        # A label needs at least two relevant cues. The score is a normalized
        # evidence value rather than a claim of psychological certainty.
        evidence = {
            "joy": min(smile, 0.75 * smile + 0.25 * max(jaw_open, 0.0)),
            "angry": min(brow_down, max(mouth_frown, mouth_press)),
            "surprised": min(max(eye_wide, jaw_open), max(brow_outer_up, brow_inner_up)),
            "sorrow": min(brow_inner_up, mouth_frown),
        }
        ranked = sorted(evidence.items(), key=lambda item: item[1], reverse=True)
        label, raw = ranked[0]
        runner_up = ranked[1][1]
        # Low absolute evidence and near ties are ambiguous facial actions,
        # not reliable apparent-expression labels.
        if raw < 0.055 or raw - runner_up < 0.025:
            return ExpressionCandidate("neutral", 0.65)

        # This is an evidence score, not a probability of a person's inner
        # state.  Avoid promoting a just-over-threshold cue to high confidence.
        return ExpressionCandidate(label, min(0.9, 0.35 + (raw - 0.055) / 0.165 * 0.55))


@dataclass
class FaceExpressionSession:
    calibration_samples: list[dict[str, float]] = field(default_factory=list)
    baseline: dict[str, float] | None = None
    candidates: Deque[ExpressionCandidate] = field(default_factory=lambda: deque(maxlen=STABILITY_WINDOW_SIZE))
    last_seen: float = field(default_factory=monotonic)
    consecutive_face_misses: int = 0

    @property
    def is_calibrated(self) -> bool:
        return self.baseline is not None

    def observe(self, blendshapes: Mapping[str, float], classifier_candidate: ExpressionCandidate | None = None) -> EmotionResult:
        self.last_seen = monotonic()
        self.consecutive_face_misses = 0
        values = {str(name): float(value) for name, value in blendshapes.items()}
        self._observe_passive_baseline(values)

        # The trained appearance classifier is the primary source. Blendshape
        # rules remain an optional fallback.  A user must never be asked to
        # perform a neutral-expression calibration before the trained model can
        # provide an apparent-expression result.
        if classifier_candidate is not None:
            candidate = classifier_candidate
        elif self.baseline is not None:
            candidate = FaceExpressionClassifier.classify(values, self.baseline)
        else:
            candidate = ExpressionCandidate("neutral", 0.65)
        self.candidates.append(candidate)
        result = self._stable_result(candidate)
        return EmotionResult(result.label, result.score, "face", "", now_unix_ms()).normalized()

    def _observe_passive_baseline(self, values: Mapping[str, float]) -> None:
        """Learn a fallback blendshape baseline silently, without gating output.

        The trained image classifier is usable immediately.  This rolling
        baseline only enables the legacy blendshape fallback for callers that
        do not supply a trained-model candidate; an expressive first few
        frames therefore cannot trap a new user in a calibration flow.
        """
        if self.baseline is not None:
            return
        self.calibration_samples.append(dict(values))
        if len(self.calibration_samples) < PASSIVE_BASELINE_SAMPLE_COUNT:
            return
        if _is_stable_calibration(self.calibration_samples):
            self.baseline = self._median_baseline(self.calibration_samples)
            self.calibration_samples.clear()
            return
        # Keep a sliding observation window.  Never clear all progress or
        # expose a calibration error merely because the user moved or smiled.
        self.calibration_samples.pop(0)

    def observe_no_face(self) -> None:
        """Forget a stale baseline after a sustained loss of the tracked face."""
        self.last_seen = monotonic()
        self.consecutive_face_misses += 1
        if self.consecutive_face_misses >= NO_FACE_RESET_COUNT:
            self.reset()

    def reset(self) -> None:
        self.calibration_samples.clear()
        self.baseline = None
        self.candidates.clear()
        self.consecutive_face_misses = 0

    @staticmethod
    def _median_baseline(samples: list[Mapping[str, float]]) -> dict[str, float]:
        names = set().union(*(sample.keys() for sample in samples))
        return {name: float(median(sample.get(name, 0.0) for sample in samples)) for name in names}

    def _stable_result(self, latest: ExpressionCandidate) -> ExpressionCandidate:
        if latest.label == "neutral":
            return latest

        matches = [item for item in self.candidates if item.label == latest.label]
        if len(matches) < STABLE_MATCH_COUNT:
            return ExpressionCandidate("neutral", 0.65)
        return ExpressionCandidate(latest.label, sum(item.score for item in matches) / len(matches))


class FaceExpressionSessions:
    """In-memory per-device smoothing and optional fallback state; no image data is retained."""

    def __init__(self) -> None:
        self._sessions: dict[str, FaceExpressionSession] = {}

    def observe(
        self,
        session_id: str,
        blendshapes: Mapping[str, float],
        classifier_candidate: ExpressionCandidate | None = None,
    ) -> EmotionResult:
        self._discard_expired()
        key = (session_id or "anonymous").strip()[:80] or "anonymous"
        if key not in self._sessions and len(self._sessions) >= MAX_SESSIONS:
            oldest = min(self._sessions, key=lambda item: self._sessions[item].last_seen)
            del self._sessions[oldest]
        session = self._sessions.setdefault(key, FaceExpressionSession())
        return session.observe(blendshapes, classifier_candidate)

    def observe_no_face(self, session_id: str) -> None:
        """Track loss without creating new state for anonymous failed requests."""
        self._discard_expired()
        key = (session_id or "anonymous").strip()[:80] or "anonymous"
        session = self._sessions.get(key)
        if session is not None:
            session.observe_no_face()

    def _discard_expired(self) -> None:
        cutoff = monotonic() - SESSION_TTL_SECONDS
        for key in [key for key, value in self._sessions.items() if value.last_seen < cutoff]:
            del self._sessions[key]
