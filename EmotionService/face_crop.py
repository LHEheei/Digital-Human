from __future__ import annotations

from typing import Tuple

import numpy


def crop_relative_face(
    image: numpy.ndarray,
    relative_box: Tuple[float, float, float, float],
    padding_ratio: float = 0.18,
) -> numpy.ndarray:
    """Crop one detected face with a small context margin, without retaining the source frame."""
    if image is None or image.ndim < 2 or image.shape[0] < 2 or image.shape[1] < 2:
        return numpy.empty((0, 0), dtype=numpy.uint8)

    image_height, image_width = image.shape[:2]
    left, top, width, height = relative_box
    margin_x = width * padding_ratio
    margin_y = height * padding_ratio
    x0 = max(0, int((left - margin_x) * image_width))
    y0 = max(0, int((top - margin_y) * image_height))
    x1 = min(image_width, int((left + width + margin_x) * image_width + 0.9999))
    y1 = min(image_height, int((top + height + margin_y) * image_height + 0.9999))
    return image[y0:y1, x0:x1].copy() if x1 > x0 and y1 > y0 else numpy.empty((0, 0), dtype=image.dtype)
