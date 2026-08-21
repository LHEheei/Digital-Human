from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy

from errors import InferenceError
from face_crop import crop_relative_face
from face_quality import assess_face_crop


MODEL_PATH = Path(__file__).parent / "models" / "face_landmarker.task"


@dataclass(frozen=True)
class FaceLandmarkObservation:
    blendshapes: dict[str, float]
    face_bgr: numpy.ndarray


class BlendshapeFaceLandmarker:
    """MediaPipe landmark adapter that exposes only numeric blendshape values."""

    def __init__(self) -> None:
        self._landmarker: Any = None
        self._face_detector: Any = None

    @property
    def loaded(self) -> bool:
        return self._landmarker is not None and self._face_detector is not None

    def warmup(self) -> None:
        self._ensure_landmarker()
        self._ensure_face_detector()

    def detect_single_face(self, image_bgr: numpy.ndarray) -> FaceLandmarkObservation | None:
        try:
            import cv2
            quality_failures: list[str] = []
            for candidate in self._orientation_candidates(image_bgr):
                # The Android WebCamTexture stream can be low contrast or contain
                # substantial background. Detecting and cropping first makes the
                # landmark model substantially less sensitive to that variation.
                crop = self._detect_single_face_crop(candidate)
                if crop is not None:
                    quality = assess_face_crop(crop)
                    if not quality.usable:
                        quality_failures.append(quality.error)
                        continue
                    values = self._detect_blendshapes(crop)
                    if values is not None:
                        return FaceLandmarkObservation(values, crop)

                # Keep an uncropped fallback for cameras where the detector's box
                # is too tight but the landmark model can still see the full face.
                values = self._detect_blendshapes(candidate)
                if values is not None:
                    return FaceLandmarkObservation(values, candidate)
            if quality_failures:
                raise InferenceError(quality_failures[0])
            return None
        except InferenceError:
            raise
        except Exception as exc:
            raise InferenceError("Face landmark model is unavailable.") from exc

    @staticmethod
    def _orientation_candidates(image_bgr: numpy.ndarray) -> tuple[numpy.ndarray, ...]:
        """Try the declared orientation first, then only right-angle fallbacks.

        Some Android camera drivers intermittently report orientation metadata
        one frame late.  These temporary arrays exist only during inference and
        are never written to disk or logged.
        """
        import cv2

        return (
            image_bgr,
            cv2.rotate(image_bgr, cv2.ROTATE_90_CLOCKWISE),
            cv2.rotate(image_bgr, cv2.ROTATE_180),
            cv2.rotate(image_bgr, cv2.ROTATE_90_COUNTERCLOCKWISE),
        )

    def _detect_single_face_crop(self, image_bgr: numpy.ndarray) -> numpy.ndarray | None:
        import cv2

        result = self._ensure_face_detector().process(cv2.cvtColor(image_bgr, cv2.COLOR_BGR2RGB))
        detections = result.detections or []
        if len(detections) != 1:
            # This detector only proposes a crop. It can occasionally produce
            # an extra low-quality background detection on Android streams;
            # defer that case to the final landmark model, which is the
            # authoritative multiple-face check.
            return None
        box = detections[0].location_data.relative_bounding_box
        crop = crop_relative_face(image_bgr, (box.xmin, box.ymin, box.width, box.height), padding_ratio=0.25)
        return crop if crop.size else None

    def _detect_blendshapes(self, image_bgr: numpy.ndarray) -> dict[str, float] | None:
        import cv2
        import mediapipe as mp

        result = self._ensure_landmarker().detect(
            mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(image_bgr, cv2.COLOR_BGR2RGB))
        )
        faces = result.face_landmarks or []
        if len(faces) > 1:
            raise InferenceError("Multiple faces detected; keep one person in frame.")
        if not faces:
            return None
        categories = (result.face_blendshapes or [[]])[0]
        return {category.category_name: float(category.score) for category in categories}

    def _ensure_landmarker(self) -> Any:
        if self._landmarker is None:
            if not MODEL_PATH.is_file():
                raise InferenceError("Face landmark model file is missing.")
            import mediapipe as mp

            options = mp.tasks.vision.FaceLandmarkerOptions(
                # MediaPipe 0.10 on Windows can incorrectly prepend its
                # package directory to an absolute asset path after a legacy
                # solution graph has been initialized. Supplying the local
                # model bytes avoids that resolver path entirely.
                base_options=mp.tasks.BaseOptions(model_asset_buffer=MODEL_PATH.read_bytes()),
                running_mode=mp.tasks.vision.RunningMode.IMAGE,
                num_faces=2,
                # The legacy detector above supplies a face crop first.  On
                # some Android front-camera streams the landmark task's
                # default 0.5 gates reject an otherwise usable, slightly
                # overexposed or soft crop.  Keep the final landmark check,
                # but lower its recall gates for that camera input.
                min_face_detection_confidence=0.25,
                min_face_presence_confidence=0.25,
                output_face_blendshapes=True,
            )
            self._landmarker = mp.tasks.vision.FaceLandmarker.create_from_options(options)
        return self._landmarker

    def _ensure_face_detector(self) -> Any:
        if self._face_detector is None:
            import mediapipe as mp

            self._face_detector = mp.solutions.face_detection.FaceDetection(
                model_selection=0,
                # This is only a crop proposal. Keep its threshold high enough
                # to avoid background false positives being reported as a
                # second person. The landmark task has its own lower recall
                # gates and orientation fallbacks below.
                min_detection_confidence=0.45,
            )
        return self._face_detector
