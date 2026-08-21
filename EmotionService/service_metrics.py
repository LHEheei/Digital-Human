from __future__ import annotations

from dataclasses import dataclass, field
from threading import Lock


@dataclass
class PerceptionMetrics:
    """In-memory, privacy-preserving request diagnostics for /health."""

    _lock: Lock = field(default_factory=Lock, init=False, repr=False)
    _requests: dict[str, int] = field(default_factory=lambda: {"face": 0, "voice": 0, "fusion": 0})
    _failures: dict[str, int] = field(default_factory=lambda: {"face": 0, "voice": 0, "fusion": 0})
    _last_face_outcome: str = "not_requested"

    def mark_request(self, source: str) -> None:
        with self._lock:
            if source in self._requests:
                self._requests[source] += 1

    def mark_failure(self, source: str) -> None:
        with self._lock:
            if source in self._failures:
                self._failures[source] += 1

    def mark_face_outcome(self, outcome: str) -> None:
        with self._lock:
            self._last_face_outcome = (outcome or "unknown")[:120]

    def snapshot(self) -> dict[str, object]:
        with self._lock:
            return {
                "requests": dict(self._requests),
                "failures": dict(self._failures),
                "lastFaceOutcome": self._last_face_outcome,
            }
