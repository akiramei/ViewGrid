"""IMAGE_VARIANT_MANAGEMENT domain model + value objects + enums.

Enums (C-ENUM = enum.Enum): Rotation, ScalingMode, Alignment, SourceType.
Value objects:
  - ImageTransform   (rotation R-09, flip_x, flip_y)
  - AutoCropSettings (R-06: both-or-neither aggregate; null = OFF)
  - ManualCropFraction (R-07: 4 values normalized; null = OFF)
  - PixelSize / OccupySize -> imported from src/shared (C-SHARED-PLACEMENT)

Entities:
  - ImageAsset (R-01, R-02)
  - ImageCopy  (R-03..R-11; R-08 declaration only)

Domain-level rules (R-04/R-05/R-06/R-07/R-09/R-10/R-11) are enforced once each,
at construction of the relevant value object / entity.
"""

from __future__ import annotations

import enum
import uuid
from dataclasses import dataclass, replace
from datetime import datetime, timezone

# C-SHARED-PLACEMENT: shared value objects (single definition).
from shared.value_objects import OccupySize, PixelSize


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


class SourceType(enum.Enum):
    LocalFile = "LocalFile"
    Url = "Url"
    Clipboard = "Clipboard"


class Rotation(enum.Enum):
    # R-09: RotationMustBeMultipleOf90 — the enum *is* the constraint.
    None_ = "None"
    CW90 = "CW90"
    CW180 = "CW180"
    CW270 = "CW270"


class ScalingMode(enum.Enum):
    # R-04: ScalingModeMustBeFromEnumeratedSet
    UniformContain = "UniformContain"
    UniformCover = "UniformCover"
    Fill = "Fill"


class Alignment(enum.Enum):
    # R-05: AlignmentMustBeFromEnumeratedSet (9 anchor points)
    TopLeft = "TopLeft"
    TopCenter = "TopCenter"
    TopRight = "TopRight"
    MiddleLeft = "MiddleLeft"
    MiddleCenter = "MiddleCenter"
    MiddleRight = "MiddleRight"
    BottomLeft = "BottomLeft"
    BottomCenter = "BottomCenter"
    BottomRight = "BottomRight"


@dataclass(frozen=True)
class ImageTransform:
    """Geometric transform. Default = Identity."""

    rotation: Rotation = Rotation.None_
    flip_x: bool = False
    flip_y: bool = False

    def __post_init__(self) -> None:
        # R-09: rotation must be one of the enumerated multiples of 90.
        if not isinstance(self.rotation, Rotation):
            raise TypeError("rotation must be a Rotation enum (R-09)")
        if not isinstance(self.flip_x, bool) or not isinstance(self.flip_y, bool):
            raise TypeError("flip_x / flip_y must be bool")


@dataclass(frozen=True)
class AutoCropSettings:
    """Aggregate value object. R-06: both target_color and threshold present.

    Modelled as a non-null aggregate; `ImageCopy.auto_crop` is `AutoCropSettings
    | None` (None = OFF). Constructing AutoCropSettings with a missing value is
    impossible by signature, so R-06's both-ness is enforced here at build time.
    """

    target_color_argb: int
    threshold: int

    def __post_init__(self) -> None:
        if isinstance(self.target_color_argb, bool) or not isinstance(self.target_color_argb, int):
            raise TypeError("target_color_argb must be an int (uint32)")
        if not (0 <= self.target_color_argb <= 0xFFFFFFFF):
            raise ValueError("target_color_argb out of uint32 range")
        if isinstance(self.threshold, bool) or not isinstance(self.threshold, int):
            raise TypeError("threshold must be an int")
        if not (0 <= self.threshold <= 255):
            raise ValueError("threshold must be in [0, 255] (R-06)")


@dataclass(frozen=True)
class ManualCropFraction:
    """Aggregate value object. R-07: 4 values in [0,1], x+w<=1, y+h<=1.

    `ImageCopy.manual_crop` is `ManualCropFraction | None` (None = OFF).
    """

    x: float
    y: float
    width: float
    height: float

    def __post_init__(self) -> None:
        for name, v in (("x", self.x), ("y", self.y),
                        ("width", self.width), ("height", self.height)):
            if isinstance(v, bool) or not isinstance(v, (int, float)):
                raise TypeError(f"{name} must be a number (R-07)")
            if not (0.0 <= float(v) <= 1.0):
                raise ValueError(f"{name} must be in [0.0, 1.0] (R-07)")
        if self.x + self.width > 1.0 + 1e-9:
            raise ValueError("x + width must be <= 1.0 (R-07)")
        if self.y + self.height > 1.0 + 1e-9:
            raise ValueError("y + height must be <= 1.0 (R-07)")


@dataclass(frozen=True)
class ImageAsset:
    id: uuid.UUID
    source_type: SourceType
    original_filename: str | None
    stored_relative_path: str
    size: PixelSize
    file_hash: str  # sha256 hex lower
    file_size_bytes: int
    mime_type: str
    created_at: datetime

    def snapshot(self) -> dict:
        return {
            "id": self.id,
            "source_type": self.source_type,
            "original_filename": self.original_filename,
            "stored_relative_path": self.stored_relative_path,
            "size": self.size,
            "file_hash": self.file_hash,
            "file_size_bytes": self.file_size_bytes,
            "mime_type": self.mime_type,
        }


@dataclass(frozen=True)
class ImageCopy:
    """Logical copy / variant. R-03..R-11 (R-08 not applied here)."""

    id: uuid.UUID
    asset_id: uuid.UUID
    copy_name: str | None
    transform: ImageTransform
    scaling_mode: ScalingMode
    alignment: Alignment
    default_occupy_size: OccupySize
    auto_crop: AutoCropSettings | None
    manual_crop: ManualCropFraction | None
    created_at: datetime
    updated_at: datetime

    def __post_init__(self) -> None:
        # R-03 (FK intent): asset_id must be present (non-null UUID).
        if not isinstance(self.asset_id, uuid.UUID):
            raise TypeError("asset_id must be a uuid.UUID (R-03)")
        # R-04 / R-05 / R-09: enum membership.
        if not isinstance(self.scaling_mode, ScalingMode):
            raise TypeError("scaling_mode must be a ScalingMode (R-04)")
        if not isinstance(self.alignment, Alignment):
            raise TypeError("alignment must be an Alignment (R-05)")
        if not isinstance(self.transform, ImageTransform):
            raise TypeError("transform must be an ImageTransform (R-09)")
        # R-10: default_occupy_size positivity is guaranteed by OccupySize itself.
        if not isinstance(self.default_occupy_size, OccupySize):
            raise TypeError("default_occupy_size must be an OccupySize (R-10)")
        # R-11: copy_name is null or non-empty (no empty string).
        if self.copy_name is not None:
            if not isinstance(self.copy_name, str):
                raise TypeError("copy_name must be str or None (R-11)")
            if len(self.copy_name) == 0:
                raise ValueError("copy_name must be None or non-empty (R-11)")
        # auto_crop / manual_crop are either None or already-validated aggregates;
        # R-06 / R-07 are enforced when those aggregates are constructed.

    def snapshot(self) -> dict:
        return {
            "id": self.id,
            "asset_id": self.asset_id,
            "copy_name": self.copy_name,
            "transform": self.transform,
            "scaling_mode": self.scaling_mode,
            "alignment": self.alignment,
            "default_occupy_size": self.default_occupy_size,
            "auto_crop": self.auto_crop,
            "manual_crop": self.manual_crop,
        }

    def with_changes(self, **changes) -> "ImageCopy":
        return replace(self, updated_at=_utc_now(), **changes)
