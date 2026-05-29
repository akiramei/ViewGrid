"""GRID_COMPOSITION canonical failure reasons (21-grid-composition.yaml).

AI must NOT invent failure reasons outside this set (FORBIDDEN).
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass, field


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
    attempted_position: "tuple[int, int]"
    occupy_size: "tuple[int, int]"


@dataclass(frozen=True)
class Conflict:
    conflicting_placement_ids: tuple[uuid.UUID, ...] = field(default_factory=tuple)


@dataclass(frozen=True)
class WouldOrphanPlacements:
    orphaned_placement_ids: tuple[uuid.UUID, ...] = field(default_factory=tuple)


@dataclass(frozen=True)
class WouldConflict:
    conflicting_placement_ids: tuple[uuid.UUID, ...] = field(default_factory=tuple)


@dataclass(frozen=True)
class UnknownCopyId:
    copy_id: uuid.UUID
