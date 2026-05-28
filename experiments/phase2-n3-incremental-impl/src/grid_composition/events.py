"""GRID_COMPOSITION events (21-grid-composition.yaml §events).

All emitted only on UseCase success (30-design.md §4). Names are canonical and
must not change. A shared synchronous in-process RecordingBus (C-EVENTBUS) lives
in src/shared via the eventbus module imported here.
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class GridCanvasCreated:
    grid_id: uuid.UUID
    snapshot: Any  # GridCanvas snapshot right after creation


@dataclass(frozen=True)
class GridDimensionsChanged:
    grid_id: uuid.UUID
    before: dict
    after: dict


@dataclass(frozen=True)
class RowColumnWeightsChanged:
    grid_id: uuid.UUID
    axis: Any  # Axis enum
    before: tuple
    after: tuple


@dataclass(frozen=True)
class RowColumnLockToggled:
    grid_id: uuid.UUID
    axis: Any  # Axis enum
    index: int
    after_state: bool


@dataclass(frozen=True)
class PlacementCreated:
    placement_id: uuid.UUID
    snapshot: Any  # Placement snapshot right after creation


@dataclass(frozen=True)
class PlacementMoved:
    placement_id: uuid.UUID
    before_position: Any  # CellPosition
    after_position: Any  # CellPosition


@dataclass(frozen=True)
class PlacementsSwapped:
    placement_id_a: uuid.UUID
    placement_id_b: uuid.UUID
    before_a: Any  # CellPosition of A before swap
    before_b: Any  # CellPosition of B before swap


@dataclass(frozen=True)
class PlacementOccupancyResized:
    placement_id: uuid.UUID
    before_size: Any  # OccupySize
    after_size: Any  # OccupySize


@dataclass(frozen=True)
class PlacementOrderChanged:
    grid_id: uuid.UUID
    before_order_map: dict  # {placement_id -> order}
    after_order_map: dict


@dataclass(frozen=True)
class PlacementRemoved:
    placement_id: uuid.UUID
    snapshot_before: Any  # Placement just before deletion
    compacted_order_map: dict  # {placement_id -> new order} for survivors
