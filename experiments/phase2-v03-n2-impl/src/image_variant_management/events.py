"""IMAGE_VARIANT_MANAGEMENT events (21 yaml §events).

Emitted only on successful state change. Names/timing fixed by spec.
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class ImageAssetImported:
    asset_id: uuid.UUID
    snapshot: dict[str, Any]


@dataclass(frozen=True)
class ImageAssetImportedAsDuplicate:
    existing_asset_id: uuid.UUID
    attempted_hash: str


@dataclass(frozen=True)
class ImageAssetDeleted:
    asset_id: uuid.UUID
    snapshot_before: dict[str, Any]


@dataclass(frozen=True)
class ImageCopyCreated:
    copy_id: uuid.UUID
    snapshot: dict[str, Any]


@dataclass(frozen=True)
class ImageCopyDeleted:
    copy_id: uuid.UUID
    snapshot_before: dict[str, Any]


@dataclass(frozen=True)
class ImageCopyTransformChanged:
    copy_id: uuid.UUID
    before: Any
    after: Any


@dataclass(frozen=True)
class ImageCopyScalingModeChanged:
    copy_id: uuid.UUID
    before: Any
    after: Any


@dataclass(frozen=True)
class ImageCopyAlignmentChanged:
    copy_id: uuid.UUID
    before: Any
    after: Any


@dataclass(frozen=True)
class ImageCopyAutoCropChanged:
    copy_id: uuid.UUID
    before: Any
    after: Any


@dataclass(frozen=True)
class ImageCopyManualCropChanged:
    copy_id: uuid.UUID
    before: Any
    after: Any


@dataclass(frozen=True)
class ImageCopyDefaultOccupySizeChanged:
    copy_id: uuid.UUID
    before: Any
    after: Any


@dataclass(frozen=True)
class ImageCopyRenamed:
    copy_id: uuid.UUID
    before: str | None
    after: str | None
