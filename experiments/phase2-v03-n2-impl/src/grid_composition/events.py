"""GRID_COMPOSITION events (21-grid-composition.yaml §events).

Emitted only on successful state change. Names and timing are fixed by spec.
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class GridCanvasCreated:
    grid_id: uuid.UUID
    snapshot: dict[str, Any]


@dataclass(frozen=True)
class GridDimensionsChanged:
    grid_id: uuid.UUID
    before: dict[str, Any]
    after: dict[str, Any]


@dataclass(frozen=True)
class RowColumnWeightsChanged:
    grid_id: uuid.UUID
    axis: str
    before_weights: tuple[int, ...]
    after_weights: tuple[int, ...]


@dataclass(frozen=True)
class RowColumnLockToggled:
    grid_id: uuid.UUID
    axis: str
    index: int
    after_state: bool


@dataclass(frozen=True)
class PlacementCreated:
    placement_id: uuid.UUID
    snapshot: dict[str, Any]


@dataclass(frozen=True)
class PlacementMoved:
    placement_id: uuid.UUID
    before_position: tuple[int, int]
    after_position: tuple[int, int]


@dataclass(frozen=True)
class PlacementsSwapped:
    placement_id_a: uuid.UUID
    placement_id_b: uuid.UUID
    before_a: tuple[int, int]
    before_b: tuple[int, int]


@dataclass(frozen=True)
class PlacementOccupancyResized:
    placement_id: uuid.UUID
    before_size: tuple[int, int]
    after_size: tuple[int, int]


@dataclass(frozen=True)
class PlacementOrderChanged:
    grid_id: uuid.UUID
    before_order_map: dict[uuid.UUID, int]
    after_order_map: dict[uuid.UUID, int]


@dataclass(frozen=True)
class PlacementRemoved:
    placement_id: uuid.UUID
    snapshot_before: dict[str, Any]
    compacted_order_map: dict[uuid.UUID, int]
