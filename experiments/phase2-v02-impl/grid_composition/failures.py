"""Canonical failure reasons (21-grid-composition.yaml §canonical_failure_reasons).

FORBIDDEN: AI may not introduce additional failure reasons. This module
mirrors the YAML list exactly. Payload shapes follow the YAML; we use
``@dataclass(frozen=True)`` values rather than exception classes so the
UseCases can return them as part of a :class:`Result` value.

MUST_DECIDE_AND_DOCUMENT: failures are values, not exceptions. The
UseCase layer does not raise on domain failure — it returns
``Err(<Failure>)``. Exceptions are reserved for programmer errors
(structural validity violations) only.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import List, Literal, Tuple, Union

from .identity import Id
from .value_objects import Axis, CellPosition, OccupySize


@dataclass(frozen=True)
class NotFound:
    """Precondition GridExists / PlacementExists violated."""

    entity_kind: Literal["GridCanvas", "Placement"]
    entity_id: Id


@dataclass(frozen=True)
class InvalidDimensions:
    """R-03 violation at UseCase entry."""

    detail: str


@dataclass(frozen=True)
class InvalidWeights:
    """R-04 / R-05 violation at UseCase entry."""

    detail: str


@dataclass(frozen=True)
class InvalidIndex:
    """UC-04 lock index out of range."""

    axis: Axis
    index: int


@dataclass(frozen=True)
class InvalidOrderValue:
    """UC-09 — order_value out of [1..N] when operation == SetOrder, or
    order_value given for an operation other than SetOrder."""

    detail: str
    attempted_value: int | None


@dataclass(frozen=True)
class OutOfBounds:
    """R-01 violation."""

    attempted_position: CellPosition
    occupy_size: OccupySize


@dataclass(frozen=True)
class Conflict:
    """R-02 violation — occupied cells intersect existing or sibling placement(s)."""

    conflicting_placement_ids: Tuple[Id, ...]


@dataclass(frozen=True)
class WouldOrphanPlacements:
    """UC-02 — dimension shrink would push existing placements out of bounds."""

    orphaned_placement_ids: Tuple[Id, ...]


@dataclass(frozen=True)
class WouldConflict:
    """UC-02 dedicated alias of Conflict per YAML."""

    conflicting_placement_ids: Tuple[Id, ...]


@dataclass(frozen=True)
class UnknownCopyId:
    """UC-05 — foreign reference (ImageCopy) not found."""

    copy_id: Id


Failure = Union[
    NotFound,
    InvalidDimensions,
    InvalidWeights,
    InvalidIndex,
    InvalidOrderValue,
    OutOfBounds,
    Conflict,
    WouldOrphanPlacements,
    WouldConflict,
    UnknownCopyId,
]
