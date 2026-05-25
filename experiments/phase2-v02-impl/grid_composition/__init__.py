"""GRID_COMPOSITION Capability — Phase 2 v0.2 implementation.

Implements the GRID_COMPOSITION Capability per
docs/capability-bom-sample/ (sample v0.2). See IMPLEMENTATION_NOTES.md.
"""

from .identity import Id, new_id
from .value_objects import Axis, CellPosition, OccupySize, OrderOperation, PixelSize
from .entities import GridCanvas, Placement
from .failures import (
    Conflict,
    Failure,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    NotFound,
    OutOfBounds,
    UnknownCopyId,
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
    RecordingBus,
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
from .use_cases import GridCompositionUseCases, Ok, Err, Result

__all__ = [
    "Axis",
    "CellPosition",
    "Conflict",
    "Err",
    "Event",
    "EventBus",
    "Failure",
    "GridCanvas",
    "GridCanvasCreated",
    "GridCanvasRepository",
    "GridCompositionUseCases",
    "GridDimensionsChanged",
    "Id",
    "ImageCopyExistenceCheck",
    "InMemoryGridCanvasRepository",
    "InMemoryImageCopyExistenceCheck",
    "InMemoryPlacementRepository",
    "InvalidDimensions",
    "InvalidIndex",
    "InvalidOrderValue",
    "InvalidWeights",
    "NotFound",
    "OccupySize",
    "Ok",
    "OrderOperation",
    "OutOfBounds",
    "PixelSize",
    "Placement",
    "PlacementCreated",
    "PlacementMoved",
    "PlacementOccupancyResized",
    "PlacementOrderChanged",
    "PlacementRemoved",
    "PlacementRepository",
    "PlacementsSwapped",
    "RecordingBus",
    "Result",
    "RowColumnLockToggled",
    "RowColumnWeightsChanged",
    "UnknownCopyId",
    "WouldConflict",
    "WouldOrphanPlacements",
    "new_id",
]
