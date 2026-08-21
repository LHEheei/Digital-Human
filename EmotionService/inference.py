from __future__ import annotations

import os
import tempfile
import threading
import wave
from typing import Any

import numpy

from audio_quality import extract_active_pcm16_wav, has_speech_like_signal, measure_pcm16_wav
from emotion_contract import EmotionResult, failure, map_voice_label, now_unix_ms
from errors import InferenceError
from face_classifier import HseFaceEmotionClassifier
from face_expression import ExpressionCandidate, FaceExpressionSessions
from face_landmarker import BlendshapeFaceLandmarker


class EmotionInference:
    """CPU emotion inference with independent face and voice execution paths."""

    def __init__(self) -> None:
        self._face_landmarker = BlendshapeFaceLandmarker()
        self._face_classifier = HseFaceEmotionClassifier()
        self._face_sessions = FaceExpressionSessions()
        self._voice_model: Any = None
        # Face sampling is time-sensitive.  Do not allow a slow voice-model load
        # or voice inference to stall camera frames behind a shared global lock.
        self._face_inference_lock = threading.Lock()
        self._voice_inference_lock = threading.Lock()

    def status(self) -> dict:
        return {
            "faceLandmarkerLoaded": self._face_landmarker.loaded,
            "faceClassifierLoaded": self._face_classifier.loaded,
            "faceClassifier": "hsemotion-enet-b0-afew-with-blendshape-calibration",
            "voiceModelLoaded": self._voice_model is not None,
            "modelLoading": "ready" if self._face_landmarker.loaded and self._face_classifier.loaded and self._voice_model is not None else "warming",
        }

    def warmup(self) -> None:
        """Load all CPU models; retained for explicit offline warmup callers."""
        self.warm_face()
        self.warm_voice()

    def warm_face(self) -> None:
        with self._face_inference_lock:
            self._face_landmarker.warmup()
            self._face_classifier.warmup()

    def warm_voice(self) -> None:
        with self._voice_inference_lock:
            self._ensure_voice_model()

    @property
    def voice_ready(self) -> bool:
        return self._voice_model is not None

    def face(self, jpeg: bytes, session_id: str = "") -> EmotionResult:
        with self._face_inference_lock:
            image = self._decode_jpeg(jpeg)
            observation = self._face_landmarker.detect_single_face(image)
            if observation is None:
                self._face_sessions.observe_no_face(session_id)
                return failure("face", "No face detected.")
            classification = self._face_classifier.classify(observation.face_bgr)
            label, score = classification.conservative_candidate()
            stable_result = self._face_sessions.observe(
                session_id,
                observation.blendshapes,
                ExpressionCandidate(label, score),
            )
            if not stable_result.usable:
                return stable_result
            return EmotionResult(
                stable_result.label,
                stable_result.score,
                stable_result.source,
                stable_result.error,
                stable_result.capturedAtUnixMs,
                classification.as_scores(),
            ).normalized()

    def voice(self, wav_bytes: bytes) -> EmotionResult:
        with self._voice_inference_lock:
            self._validate_wav(wav_bytes)
            quality = measure_pcm16_wav(wav_bytes)
            if not has_speech_like_signal(quality):
                raise InferenceError("No clear speech detected; speak closer to the microphone.")
            path = self._write_temporary_wav(extract_active_pcm16_wav(wav_bytes, quality))
            try:
                generated = self._ensure_voice_model().generate(
                    input=path,
                    granularity="utterance",
                    extract_embedding=False,
                )
                label, score = self._extract_voice_prediction(generated)
                return EmotionResult(map_voice_label(label), score, "voice", "", now_unix_ms()).normalized()
            except InferenceError:
                raise
            except Exception as exc:
                raise InferenceError(f"Voice emotion model unavailable: {type(exc).__name__}") from exc
            finally:
                if os.path.exists(path):
                    os.remove(path)

    @staticmethod
    def _decode_jpeg(jpeg: bytes) -> numpy.ndarray:
        if not jpeg or len(jpeg) < 128:
            raise InferenceError("Image payload is empty or invalid.")
        try:
            import cv2

            image = cv2.imdecode(numpy.frombuffer(jpeg, dtype=numpy.uint8), cv2.IMREAD_COLOR)
        except Exception as exc:
            raise InferenceError("Image decoder is unavailable.") from exc
        if image is None:
            raise InferenceError("Image payload is not a valid JPEG.")
        return image

    def _ensure_voice_model(self) -> Any:
        if self._voice_model is None:
            from funasr import AutoModel

            self._voice_model = AutoModel(
                model="iic/emotion2vec_plus_base",
                device="cpu",
                disable_update=True,
            )
        return self._voice_model

    @staticmethod
    def _validate_wav(wav_bytes: bytes) -> None:
        if len(wav_bytes) < 44:
            raise InferenceError("Audio payload is empty or not WAV.")
        try:
            with wave.open(__import__("io").BytesIO(wav_bytes), "rb") as reader:
                if reader.getnchannels() != 1 or reader.getframerate() != 16_000 or reader.getsampwidth() != 2:
                    raise InferenceError("WAV must be 16 kHz, mono, PCM16.")
                if reader.getnframes() < 16_000:
                    raise InferenceError("Audio is shorter than one second.")
        except wave.Error as exc:
            raise InferenceError("Audio payload is not a valid WAV.") from exc

    @staticmethod
    def _write_temporary_wav(wav_bytes: bytes) -> str:
        file = tempfile.NamedTemporaryFile(prefix="srtp-emotion-", suffix=".wav", delete=False)
        try:
            file.write(wav_bytes)
            return file.name
        finally:
            file.close()

    @staticmethod
    def _extract_voice_prediction(generated: Any) -> tuple[str, float]:
        result = generated[0] if isinstance(generated, list) and generated else generated
        if not isinstance(result, dict):
            raise InferenceError("Voice model returned no prediction.")
        labels = result.get("labels") or result.get("label") or []
        scores = result.get("scores") or result.get("score") or []
        if not isinstance(labels, list) or not isinstance(scores, list) or not labels or not scores:
            raise InferenceError("Voice model returned no emotion label.")
        if len(labels) != len(scores):
            raise InferenceError("Voice model returned mismatched labels and scores.")
        index, score = max(enumerate(scores), key=lambda item: float(item[1]))
        return str(labels[index]), float(score)
