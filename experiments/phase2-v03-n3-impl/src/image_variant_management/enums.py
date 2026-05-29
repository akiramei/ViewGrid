"""IMAGE_VARIANT_MANAGEMENT enums (C-ENUM: enum.Enum).

Names follow 20-capability-bom.md §4.4 / 21 yaml exactly.
"""
from __future__ import annotations

import enum


class Rotation(enum.Enum):
    # R-09: RotationMustBeMultipleOf90.
    NoRotation = "None"  # member name avoids Python keyword; .value is the neutral str "None"
    CW90 = "CW90"
    CW180 = "CW180"
    CW270 = "CW270"


class ScalingMode(enum.Enum):
    # R-04: ScalingModeMustBeFromEnumeratedSet.
    UniformContain = "UniformContain"
    UniformCover = "UniformCover"
    Fill = "Fill"


class Alignment(enum.Enum):
    # R-05: AlignmentMustBeFromEnumeratedSet (9 anchors).
    TopLeft = "TopLeft"
    TopCenter = "TopCenter"
    TopRight = "TopRight"
    MiddleLeft = "MiddleLeft"
    MiddleCenter = "MiddleCenter"
    MiddleRight = "MiddleRight"
    BottomLeft = "BottomLeft"
    BottomCenter = "BottomCenter"
    BottomRight = "BottomRight"
