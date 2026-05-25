"""GRID_COMPOSITION Capability.

Implements the GRID_COMPOSITION Capability per the Capability BOM at
docs/capability-bom-sample/. Public surface is exposed here for callers; all
Decision Ownership boundaries are enforced by the submodule layout:

- domain/         — Entities and value objects (owns R-03, R-04, R-05, R-07)
- rules/          — Pure-function Rule predicates (owns R-01, R-02, R-06, R-09)
- use_cases/      — UC-01..UC-11 orchestration (enforces Rules, emits Events)
- events/         — Canonical Event types + in-process EventBus
- repositories/   — Repository protocols + in-memory stubs
- errors.py       — Canonical failure-reason names (must not be renamed)
"""

from .domain import (
    CellPosition,
    GridCanvas,
    OccupySize,
    PixelSize,
    Placement,
)
from .errors import (
    Conflict,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    OutOfBounds,
    UnknownCopyId,
    UseCaseError,
    WouldConflict,
    WouldOrphanPlacements,
)
from .events import (
    Event,
    EventBus,
    GridCanvasCreated,
    GridDimensionsChanged,
    PlacementCreated,
    PlacementMoved,
    PlacementOccupancyResized,
    PlacementOrderChanged,
    PlacementRemoved,
    PlacementsSwapped,
    RowColumnLockToggled,
    RowColumnWeightsChanged,
)
from .repositories import (
    GridCanvasRepository,
    ImageCopyExistenceCheck,
    InMemoryGridCanvasRepository,
    InMemoryImageCopyExistenceCheck,
    InMemoryPlacementRepository,
    PlacementRepository,
)
from .use_cases import (
    ChangeGridDimensions,
    ChangePlacementOrder,
    ChangeRowColumnWeights,
    CreateGridCanvas,
    ListPlacements,
    MovePlacement,
    PlaceImageCopy,
    RemovePlacement,
    ResizePlacementOccupancy,
    SwapPlacements,
    ToggleRowColumnLock,
)

__all__ = [
    # domain
    "CellPosition",
    "OccupySize",
    "PixelSize",
    "GridCanvas",
    "Placement",
    # errors (canonical failure-reason names from BOM)
    "UseCaseError",
    "InvalidDimensions",
    "InvalidWeights",
    "InvalidIndex",
    "InvalidOrderValue",
    "OutOfBounds",
    "Conflict",
    "UnknownCopyId",
    "WouldOrphanPlacements",
    "WouldConflict",
    # events (canonical names)
    "Event",
    "EventBus",
    "GridCanvasCreated",
    "GridDimensionsChanged",
    "RowColumnWeightsChanged",
    "RowColumnLockToggled",
    "PlacementCreated",
    "PlacementMoved",
    "PlacementsSwapped",
    "PlacementOccupancyResized",
    "PlacementOrderChanged",
    "PlacementRemoved",
    # repositories
    "GridCanvasRepository",
    "PlacementRepository",
    "ImageCopyExistenceCheck",
    "InMemoryGridCanvasRepository",
    "InMemoryPlacementRepository",
    "InMemoryImageCopyExistenceCheck",
    # use cases
    "CreateGridCanvas",
    "ChangeGridDimensions",
    "ChangeRowColumnWeights",
    "ToggleRowColumnLock",
    "PlaceImageCopy",
    "MovePlacement",
    "SwapPlacements",
    "ResizePlacementOccupancy",
    "ChangePlacementOrder",
    "RemovePlacement",
    "ListPlacements",
]
