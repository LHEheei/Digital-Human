from __future__ import annotations

from dataclasses import dataclass

import numpy


@dataclass(frozen=True)
class FaceImageQuality:
    """Non-identifying image quality signals for a cropped face frame."""

    brightness: float
    sharpness: float
    shortest_edge: int

    @property
    def error(self) -> str:
        if self.shortest_edge < 96:
            return "Face is too small; move closer to the camera."
        if self.brightness < 0.10:
            return "Face image is too dark; improve the lighting."
        if self.brightness > 0.95:
            return "Face image is overexposed; avoid strong backlight."
        if self.sharpness < 8.0:
            return "Face image is too blurry; keep the camera steady."
        return ""

    @property
    def usable(self) -> bool:
        return not self.error


def assess_face_crop(image_bgr: numpy.ndarray) -> FaceImageQuality:
    """Measure only transient quality statistics; never retain image pixels."""
    if image_bgr is None or image_bgr.size == 0:
        return FaceImageQuality(0.0, 0.0, 0)

    import cv2

    gray = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2GRAY)
    return FaceImageQuality(
        brightness=float(numpy.mean(gray)) / 255.0,
        sharpness=float(cv2.Laplacian(gray, cv2.CV_64F).var()),
        shortest_edge=int(min(image_bgr.shape[:2])),
    )
