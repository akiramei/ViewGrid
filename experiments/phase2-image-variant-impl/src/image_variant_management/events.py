"""Events emitted by IMAGE_VARIANT_MANAGEMENT.

Event names and emission timing are FORBIDDEN to change (per 40-prompt §FORBIDDEN
and 21-yaml events).

Payloads carry snapshots so subscribers do not need to query the Capability
later. The dispatch mechanism is an in-memory ``EventBus`` (AI choice — see
IMPLEMENTATION_NOTES.md).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Optional

from .domain import (
    Alignment,
    AutoCropSettings,
    ImageAsset,
    ImageCopy,
    ImageTransform,
    ManualCropFraction,
    ScalingMode,
)
from .shared import OccupySize


# ----------------------------------------------------------------------------
# Event types
# ----------------------------------------------------------------------------


@dataclass(frozen=True, slots=True)
class ImageAssetImported:
    """UC-01 success, new ImageAsset created."""

    asset_id: str
    snapshot: ImageAsset


@dataclass(frozen=True, slots=True)
class ImageAssetImportedAsDuplicate:
    """UC-01 success, but hash matched an existing ImageAsset (R-02)."""

    existing_asset_id: str
    attempted_hash: str


@dataclass(frozen=True, slots=True)
class ImageAssetDeleted:
    """UC-02 success."""

    asset_id: str
    snapshot_before: ImageAsset


@dataclass(frozen=True, slots=True)
class ImageCopyCreated:
    """UC-05 success."""

    copy_id: str
    snapshot: ImageCopy


@dataclass(frozen=True, slots=True)
class ImageCopyDeleted:
    """UC-06 success."""

    copy_id: str
    snapshot_before: ImageCopy


@dataclass(frozen=True, slots=True)
class ImageCopyTransformChanged:
    """UC-09."""

    copy_id: str
    before: ImageTransform
    after: ImageTransform


@dataclass(frozen=True, slots=True)
class ImageCopyScalingModeChanged:
    """UC-10."""

    copy_id: str
    before: ScalingMode
    after: ScalingMode


@dataclass(frozen=True, slots=True)
class ImageCopyAlignmentChanged:
    """UC-11."""

    copy_id: str
    before: Alignment
    after: Alignment


@dataclass(frozen=True, slots=True)
class ImageCopyAutoCropChanged:
    """UC-12."""

    copy_id: str
    before: Optional[AutoCropSettings]
    after: Optional[AutoCropSettings]


@dataclass(frozen=True, slots=True)
class ImageCopyManualCropChanged:
    """UC-13."""

    copy_id: str
    before: Optional[ManualCropFraction]
    after: Optional[ManualCropFraction]


@dataclass(frozen=True, slots=True)
class ImageCopyDefaultOccupySizeChanged:
    """UC-14."""

    copy_id: str
    before: OccupySize
    after: OccupySize


@dataclass(frozen=True, slots=True)
class ImageCopyRenamed:
    """UC-15."""

    copy_id: str
    before: Optional[str]
    after: Optional[str]


# ----------------------------------------------------------------------------
# EventBus (minimal pub/sub)
# ----------------------------------------------------------------------------


class EventBus:
    """In-memory event bus.

    Decision: this Capability uses a simple synchronous in-memory bus for the
    PoC. Subscribers register a callback; ``publish`` records the event into
    ``recorded`` (test instrumentation) and dispatches synchronously.

    The bus is intentionally *separate* from state mutation in the UseCase
    layer so emission is testable independently of repository writes.
    """

    def __init__(self) -> None:
        self._subscribers: list[Callable[[object], None]] = []
        self.recorded: list[object] = []

    def subscribe(self, callback: Callable[[object], None]) -> None:
        self._subscribers.append(callback)

    def publish(self, event: object) -> None:
        self.recorded.append(event)
        for sub in list(self._subscribers):
            sub(event)

    def clear(self) -> None:
        self.recorded.clear()
