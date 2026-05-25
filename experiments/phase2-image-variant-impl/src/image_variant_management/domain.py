"""Domain Model — Entities, value objects, and enums for IMAGE_VARIANT_MANAGEMENT.

Each invariant from ``20-capability-bom.md`` §3 (R-04..R-07, R-09..R-11) is
enforced at construction time of the relevant Domain Model object. UseCase-layer
invariants (R-01..R-03) are enforced in ``use_cases.py``.

R-08 (ManualCropOverridesAutoCrop) is **declaration only** in this Capability:
this module deliberately allows ``ImageCopy`` to hold a non-null ``auto_crop``
and a non-null ``manual_crop`` simultaneously. The rendering interpretation of
the override is owned by RENDERING_EXPORT (see ``20-capability-bom.md`` §6).
"""

from __future__ import annotations

from dataclasses import dataclass, field, replace
from datetime import datetime, timezone
from enum import Enum
from typing import Optional

from .shared import OccupySize, PixelSize


# ----------------------------------------------------------------------------
# Enums  (R-04, R-05, R-09)
# ----------------------------------------------------------------------------


class Rotation(Enum):
    """R-09: rotation must be multiple of 90 degrees, expressed as enum."""

    NONE = "None"
    CW90 = "CW90"
    CW180 = "CW180"
    CW270 = "CW270"


class ScalingMode(Enum):
    """R-04: enumerated scaling mode."""

    UNIFORM_CONTAIN = "UniformContain"
    UNIFORM_COVER = "UniformCover"
    FILL = "Fill"


class Alignment(Enum):
    """R-05: 9-point anchor (top/middle/bottom × left/center/right)."""

    TOP_LEFT = "TopLeft"
    TOP_CENTER = "TopCenter"
    TOP_RIGHT = "TopRight"
    MIDDLE_LEFT = "MiddleLeft"
    MIDDLE_CENTER = "MiddleCenter"
    MIDDLE_RIGHT = "MiddleRight"
    BOTTOM_LEFT = "BottomLeft"
    BOTTOM_CENTER = "BottomCenter"
    BOTTOM_RIGHT = "BottomRight"


class SourceType(Enum):
    """Where the ImageAsset was imported from."""

    LOCAL_FILE = "LocalFile"
    URL = "Url"
    CLIPBOARD = "Clipboard"
    UNKNOWN = "Unknown"


# ----------------------------------------------------------------------------
# Value Objects
# ----------------------------------------------------------------------------


@dataclass(frozen=True, slots=True)
class ImageTransform:
    """Rotation + flip. Default (identity) is (NONE, false, false)."""

    rotation: Rotation = Rotation.NONE
    flip_x: bool = False
    flip_y: bool = False

    def __post_init__(self) -> None:
        if not isinstance(self.rotation, Rotation):
            # R-09 — Rotation enum guard, even when constructed via raw call.
            raise ValueError(f"rotation must be Rotation enum, got {type(self.rotation).__name__}")
        if not isinstance(self.flip_x, bool) or not isinstance(self.flip_y, bool):
            raise TypeError("flip_x/flip_y must be bool")


@dataclass(frozen=True, slots=True)
class AutoCropSettings:
    """R-06: aggregate value object. Both fields meaningful only together.

    ImageCopy.auto_crop is ``AutoCropSettings | None``; ``None`` means "OFF".
    Construction of a non-None value asserts both fields are present and in
    range.
    """

    target_color_argb: int  # uint32
    threshold: int  # uint8 [0, 255]

    def __post_init__(self) -> None:
        if not isinstance(self.target_color_argb, int):
            raise TypeError("target_color_argb must be int")
        if not (0 <= self.target_color_argb <= 0xFFFFFFFF):
            raise ValueError(
                f"target_color_argb must be in uint32 range; got {self.target_color_argb}"
            )
        if not isinstance(self.threshold, int):
            raise TypeError("threshold must be int")
        if not (0 <= self.threshold <= 255):
            raise ValueError(f"threshold must be in [0, 255]; got {self.threshold}")


@dataclass(frozen=True, slots=True)
class ManualCropFraction:
    """R-07: all four fractions in [0,1]; x+w <= 1.0 and y+h <= 1.0.

    Aggregate semantics: all 4 fields must be non-null together (this is enforced
    structurally — the dataclass simply cannot exist without all four). The
    UC-13 layer enforces the "all-4-null = OFF" semantics by setting the field
    on ``ImageCopy`` to ``None``.
    """

    x: float
    y: float
    width: float
    height: float

    def __post_init__(self) -> None:
        for name, value in (("x", self.x), ("y", self.y), ("width", self.width), ("height", self.height)):
            if not isinstance(value, (int, float)) or isinstance(value, bool):
                raise TypeError(f"{name} must be a number, got {type(value).__name__}")
            if not (0.0 <= float(value) <= 1.0):
                raise ValueError(f"{name} must be in [0.0, 1.0]; got {value}")
        if self.x + self.width > 1.0 + 1e-12:
            raise ValueError(
                f"x + width must be <= 1.0; got x={self.x}, width={self.width}"
            )
        if self.y + self.height > 1.0 + 1e-12:
            raise ValueError(
                f"y + height must be <= 1.0; got y={self.y}, height={self.height}"
            )


# ----------------------------------------------------------------------------
# Entities  (Domain Model)
# ----------------------------------------------------------------------------


@dataclass(frozen=True, slots=True)
class ImageAsset:
    """Original image metadata. Invariants R-01, R-02 are UseCase-enforced;
    field shape is enforced here.
    """

    id: str  # identity
    source_type: SourceType
    original_filename: Optional[str]
    stored_relative_path: str
    size: PixelSize
    file_hash: str  # SHA-256 hex lower
    file_size_bytes: int
    mime_type: str
    created_at: datetime

    def __post_init__(self) -> None:
        if not self.id:
            raise ValueError("ImageAsset.id must be non-empty")
        if not isinstance(self.source_type, SourceType):
            raise TypeError("source_type must be SourceType enum")
        if not isinstance(self.size, PixelSize):
            raise TypeError("size must be PixelSize")
        if not self.stored_relative_path:
            raise ValueError("stored_relative_path must be non-empty")
        if not (isinstance(self.file_hash, str) and len(self.file_hash) == 64):
            raise ValueError("file_hash must be 64-char hex (sha256)")
        if self.file_hash != self.file_hash.lower():
            raise ValueError("file_hash must be lowercase hex")
        if self.file_size_bytes < 0:
            raise ValueError("file_size_bytes must be >= 0")
        if not self.mime_type:
            raise ValueError("mime_type must be non-empty")


@dataclass(frozen=True, slots=True)
class ImageCopy:
    """Logical copy (derived view) of an ImageAsset.

    Invariants (enforced here at construction):
      - R-03 partial: asset_id non-empty
      - R-04, R-05, R-09: enum-typed fields
      - R-06: ``auto_crop`` is either ``None`` or a valid AutoCropSettings
      - R-07: ``manual_crop`` is either ``None`` or a valid ManualCropFraction
      - R-10: default_occupy_size positive (enforced by OccupySize itself)
      - R-11: copy_name is None or a non-empty string

    R-08 (ManualCropOverridesAutoCrop) is intentionally **not** enforced here:
    both ``auto_crop`` and ``manual_crop`` may be non-None simultaneously.
    """

    id: str
    asset_id: str
    copy_name: Optional[str]
    transform: ImageTransform
    scaling_mode: ScalingMode
    alignment: Alignment
    default_occupy_size: OccupySize
    auto_crop: Optional[AutoCropSettings]
    manual_crop: Optional[ManualCropFraction]
    created_at: datetime
    updated_at: datetime

    def __post_init__(self) -> None:
        if not self.id:
            raise ValueError("ImageCopy.id must be non-empty")
        if not self.asset_id:
            # R-03 (Domain-layer partial guarantee): asset_id is required.
            raise ValueError("ImageCopy.asset_id must reference an asset")
        if not isinstance(self.transform, ImageTransform):
            raise TypeError("transform must be ImageTransform")
        if not isinstance(self.scaling_mode, ScalingMode):
            # R-04
            raise TypeError("scaling_mode must be ScalingMode")
        if not isinstance(self.alignment, Alignment):
            # R-05
            raise TypeError("alignment must be Alignment")
        if not isinstance(self.default_occupy_size, OccupySize):
            # R-10 — OccupySize itself enforces positivity.
            raise TypeError("default_occupy_size must be OccupySize")
        if self.auto_crop is not None and not isinstance(self.auto_crop, AutoCropSettings):
            raise TypeError("auto_crop must be AutoCropSettings or None")
        if self.manual_crop is not None and not isinstance(self.manual_crop, ManualCropFraction):
            raise TypeError("manual_crop must be ManualCropFraction or None")
        # R-11: copy_name is None OR non-empty string.
        if self.copy_name is not None:
            if not isinstance(self.copy_name, str):
                raise TypeError("copy_name must be str or None")
            if self.copy_name == "":
                raise ValueError("copy_name must be None or non-empty (R-11)")

    # Convenience updater (returns a new frozen instance with updated_at bumped).
    def with_changes(self, *, now: datetime, **changes: object) -> "ImageCopy":
        changes.setdefault("updated_at", now)
        return replace(self, **changes)  # type: ignore[arg-type]


def utc_now() -> datetime:
    """Default timestamp source (UTC). See IMPLEMENTATION_NOTES.md."""

    return datetime.now(timezone.utc)
