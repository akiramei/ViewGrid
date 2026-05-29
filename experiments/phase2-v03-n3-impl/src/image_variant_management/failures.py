"""IMAGE_VARIANT_MANAGEMENT canonical failure reasons (21 yaml).

AI must NOT invent failure reasons outside this set (FORBIDDEN).
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass, field


@dataclass(frozen=True)
class NotFound:
    entity_kind: str  # "ImageAsset" | "ImageCopy"
    entity_id: uuid.UUID


@dataclass(frozen=True)
class DependentCopiesExist:
    asset_id: uuid.UUID
    dependent_copy_ids: tuple[uuid.UUID, ...] = field(default_factory=tuple)


@dataclass(frozen=True)
class InvalidImageData:
    detail: str


@dataclass(frozen=True)
class UnsupportedMimeType:
    attempted_mime_type: str
    supported_mime_types: tuple[str, ...]


@dataclass(frozen=True)
class InvalidAlignment:
    attempted_value: str


@dataclass(frozen=True)
class InvalidScalingMode:
    attempted_value: str


@dataclass(frozen=True)
class InvalidTransform:
    attempted_rotation: str
    attempted_flip_x: bool
    attempted_flip_y: bool


@dataclass(frozen=True)
class InvalidOccupySize:
    attempted_width: int
    attempted_height: int


@dataclass(frozen=True)
class InvalidAutoCropSettings:
    detail: str
    attempted_target_color: int | None
    attempted_threshold: int | None


@dataclass(frozen=True)
class InvalidManualCropFractions:
    detail: str
    x: float | None
    y: float | None
    width: float | None
    height: float | None


@dataclass(frozen=True)
class InvalidCopyName:
    detail: str
    attempted_value: str | None
