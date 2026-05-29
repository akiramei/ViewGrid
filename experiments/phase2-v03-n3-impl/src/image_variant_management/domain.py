"""IMAGE_VARIANT_MANAGEMENT domain model.

Entities:
  - ImageAsset (R-01, R-02 are UseCase-layer; entity just holds validated data)
  - ImageCopy  (R-03..R-07, R-09..R-11 enforced at construction)

Value objects:
  - ImageTransform (R-09 rotation enum + flips)
  - AutoCropSettings (R-06 both-or-neither aggregate)
  - ManualCropFraction (R-07 normalized fractions)

PixelSize / OccupySize are imported from shared (NOT redefined).
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone

from shared.value_objects import OccupySize, PixelSize  # noqa: F401 (shared VO)
from .enums import Alignment, Rotation, ScalingMode


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


@dataclass(frozen=True)
class ImageTransform:
    rotation: Rotation = Rotation.NoRotation
    flip_x: bool = False
    flip_y: bool = False

    def __post_init__(self) -> None:
        # R-09: rotation must be a Rotation enum member.
        if not isinstance(self.rotation, Rotation):
            raise ValueError("rotation must be a Rotation enum member")
        if not isinstance(self.flip_x, bool) or not isinstance(self.flip_y, bool):
            raise ValueError("flip_x / flip_y must be bool")


@dataclass(frozen=True)
class AutoCropSettings:
    """Aggregate value: both values meaningful (R-06)."""

    target_color_argb: int
    threshold: int

    def __post_init__(self) -> None:
        # R-06 is enforced at the *aggregate* boundary: this object only exists
        # when both values are present. Range check on threshold.
        if isinstance(self.target_color_argb, bool) or not isinstance(self.target_color_argb, int):
            raise ValueError("target_color_argb must be a uint32 int")
        if not (0 <= self.target_color_argb <= 0xFFFFFFFF):
            raise ValueError("target_color_argb out of uint32 range")
        if isinstance(self.threshold, bool) or not isinstance(self.threshold, int):
            raise ValueError("threshold must be an int")
        if not (0 <= self.threshold <= 255):
            raise ValueError("threshold must be in [0, 255]")


@dataclass(frozen=True)
class ManualCropFraction:
    """Normalized crop bbox (R-07)."""

    x: float
    y: float
    width: float
    height: float

    def __post_init__(self) -> None:
        for name in ("x", "y", "width", "height"):
            v = getattr(self, name)
            if isinstance(v, bool) or not isinstance(v, (int, float)):
                raise ValueError(f"{name} must be a number")
            if not (0.0 <= float(v) <= 1.0):
                raise ValueError(f"{name} must be in [0.0, 1.0]")
        if self.x + self.width > 1.0:
            raise ValueError("x + width must be <= 1.0")
        if self.y + self.height > 1.0:
            raise ValueError("y + height must be <= 1.0")


@dataclass(frozen=True)
class ImageAsset:
    id: uuid.UUID
    source_type: str
    original_filename: str | None
    stored_relative_path: str
    size: PixelSize
    file_hash: str
    file_size_bytes: int
    mime_type: str
    created_at: datetime = field(default_factory=_utc_now)


@dataclass(frozen=True)
class ImageCopy:
    id: uuid.UUID
    asset_id: uuid.UUID
    copy_name: str | None
    transform: ImageTransform
    scaling_mode: ScalingMode
    alignment: Alignment
    default_occupy_size: OccupySize
    auto_crop: AutoCropSettings | None
    manual_crop: ManualCropFraction | None
    created_at: datetime = field(default_factory=_utc_now)
    updated_at: datetime = field(default_factory=_utc_now)

    def __post_init__(self) -> None:
        # R-03: asset_id non-null (FK intent).
        if self.asset_id is None:
            raise ValueError("asset_id must reference an existing ImageAsset")
        # R-04 / R-05 / R-09 via enum types.
        if not isinstance(self.scaling_mode, ScalingMode):
            raise ValueError("scaling_mode must be a ScalingMode enum member")
        if not isinstance(self.alignment, Alignment):
            raise ValueError("alignment must be an Alignment enum member")
        if not isinstance(self.transform, ImageTransform):
            raise ValueError("transform must be an ImageTransform")
        # R-10: DefaultOccupySizeMustBePositive (delegated to OccupySize VO).
        if not isinstance(self.default_occupy_size, OccupySize):
            raise ValueError("default_occupy_size must be an OccupySize")
        # R-11: CopyName must be null or non-empty.
        if self.copy_name is not None and self.copy_name == "":
            raise ValueError("copy_name must be null or non-empty")
