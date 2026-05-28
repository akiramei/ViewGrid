"""GRID_COMPOSITION canonical failure reasons.

Names and payload shapes are fixed by 21-grid-composition.yaml
§canonical_failure_reasons. The AI MUST NOT invent new failure reasons.
Each is a frozen dataclass wrapped in shared.result.Err.
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass

from grid_composition.domain import CellPosition
from shared.value_objects import OccupySize


@dataclass(frozen=True)
class NotFound:
    entity_kind: str  # "GridCanvas" | "Placement"
    entity_id: uuid.UUID


@dataclass(frozen=True)
class InvalidDimensions:
    detail: str


@dataclass(frozen=True)
class InvalidWeights:
    detail: str


@dataclass(frozen=True)
class InvalidIndex:
    axis: str  # "Row" | "Col"
    index: int


@dataclass(frozen=True)
class InvalidOrderValue:
    detail: str
    attempted_value: int | None


@dataclass(frozen=True)
class OutOfBounds:
    attempted_position: CellPosition
    occupy_size: OccupySize


@dataclass(frozen=True)
class Conflict:
    conflicting_placement_ids: tuple[uuid.UUID, ...]


@dataclass(frozen=True)
class WouldOrphanPlacements:
    orphaned_placement_ids: tuple[uuid.UUID, ...]


@dataclass(frozen=True)
class WouldConflict:
    conflicting_placement_ids: tuple[uuid.UUID, ...]


@dataclass(frozen=True)
class UnknownCopyId:
    copy_id: uuid.UUID
