"""Domain Model — Entities and Value Objects.

Decision Ownership per 21-grid-composition.yaml §decision_ownership:
- This module owns `domain_decision` for invariants enforceable at
  construction time: R-03, R-04, R-05 (length match), and R-07
  (immutability of position/occupy_size within one Placement).
- R-01, R-02, R-06, R-09 are NOT owned here — they require knowledge of
  other placements and live in the rules/ module (use_case layer).

Entities are frozen dataclasses. To "change" a field, callers construct a new
instance (see Placement.with_position, etc.). This satisfies R-07 by
construction: a Placement instance never mutates.

Validation in __post_init__ raises ValueError carrying a canonical
detail string. Use cases convert these to the canonical UseCaseError
subclasses (InvalidDimensions / InvalidWeights). This split keeps the domain
layer free of validation_decision but lets it refuse to build illegal
instances (Rule R-03/R-04/R-05 guarantee at the Entity boundary).
"""

from __future__ import annotations

from dataclasses import dataclass, field, replace
from datetime import datetime, timezone
from typing import Sequence
from uuid import UUID, uuid4


# -- Value objects ----------------------------------------------------------


@dataclass(frozen=True, slots=True)
class CellPosition:
    """0-based cell coordinate. x = column, y = row (graphics convention).

    Semantically guaranteed: x >= 0, y >= 0. Bound checking against the host
    grid's dimensions is R-01 and lives in rules.placement_fits_within_grid.
    """

    x: int
    y: int

    def __post_init__(self) -> None:
        if self.x < 0 or self.y < 0:
            raise ValueError(f"CellPosition coordinates must be >= 0 (got x={self.x}, y={self.y})")


@dataclass(frozen=True, slots=True)
class OccupySize:
    """Cell-count footprint. width = cols, height = rows. Both >= 1."""

    width: int
    height: int

    def __post_init__(self) -> None:
        if self.width < 1 or self.height < 1:
            raise ValueError(
                f"OccupySize width and height must be >= 1 (got width={self.width}, height={self.height})"
            )


@dataclass(frozen=True, slots=True)
class PixelSize:
    """Final canvas output size in px. Both > 0. The Capability stores this
    but does not interpret it; interpretation belongs to RENDERING_EXPORT."""

    width: int
    height: int

    def __post_init__(self) -> None:
        if self.width <= 0 or self.height <= 0:
            raise ValueError(
                f"PixelSize width and height must be > 0 (got width={self.width}, height={self.height})"
            )


# -- Entities ---------------------------------------------------------------


def _now() -> datetime:
    """Centralized clock. Decision: UTC, tz-aware datetime.

    Documented in IMPLEMENTATION_NOTES.md as a MUST_DECIDE_AND_DOCUMENT
    decision per 90-feasibility-notes.md §2.2 item 3.
    """

    return datetime.now(timezone.utc)


def _new_id() -> UUID:
    return uuid4()


@dataclass(frozen=True, slots=True)
class GridCanvas:
    """N rows x M cols grid plus output canvas size.

    Construction-time invariants (R-03, R-04, R-05):
    - grid_rows >= 1, grid_cols >= 1 (R-03)
    - canvas_size.width > 0, canvas_size.height > 0 (R-03 — enforced by PixelSize)
    - col_weights / row_weights are positive integers (R-04)
    - col_weights length == grid_cols, row_weights length == grid_rows (R-05)
    - col_locked length == grid_cols, row_locked length == grid_rows
    """

    id: UUID
    name: str
    grid_rows: int
    grid_cols: int
    col_weights: tuple[int, ...]
    row_weights: tuple[int, ...]
    col_locked: tuple[bool, ...]
    row_locked: tuple[bool, ...]
    canvas_size: PixelSize
    created_at: datetime
    updated_at: datetime

    def __post_init__(self) -> None:
        # R-03
        if self.grid_rows < 1:
            raise ValueError(f"grid_rows must be >= 1 (got {self.grid_rows})")
        if self.grid_cols < 1:
            raise ValueError(f"grid_cols must be >= 1 (got {self.grid_cols})")
        # R-04
        if not all(isinstance(w, int) and w > 0 for w in self.col_weights):
            raise ValueError(f"col_weights must be positive ints (got {self.col_weights})")
        if not all(isinstance(w, int) and w > 0 for w in self.row_weights):
            raise ValueError(f"row_weights must be positive ints (got {self.row_weights})")
        # R-05
        if len(self.col_weights) != self.grid_cols:
            raise ValueError(
                f"col_weights length {len(self.col_weights)} != grid_cols {self.grid_cols}"
            )
        if len(self.row_weights) != self.grid_rows:
            raise ValueError(
                f"row_weights length {len(self.row_weights)} != grid_rows {self.grid_rows}"
            )
        # Lock-array length matches dimension (semantic invariant per
        # 30-design.md §3.1 — not listed as a numbered Rule, treated as part
        # of the GridCanvas shape guarantee).
        if len(self.col_locked) != self.grid_cols:
            raise ValueError(
                f"col_locked length {len(self.col_locked)} != grid_cols {self.grid_cols}"
            )
        if len(self.row_locked) != self.grid_rows:
            raise ValueError(
                f"row_locked length {len(self.row_locked)} != grid_rows {self.grid_rows}"
            )

    # Constructors --------------------------------------------------------

    @classmethod
    def create(
        cls,
        name: str,
        grid_rows: int,
        grid_cols: int,
        canvas_size: PixelSize,
        *,
        id: UUID | None = None,
        now: datetime | None = None,
    ) -> "GridCanvas":
        """Create a new GridCanvas with default uniform weights (all 1) and
        all-unlocked rows/cols. Use cases call this from UC-01."""

        ts = now if now is not None else _now()
        return cls(
            id=id or _new_id(),
            name=name,
            grid_rows=grid_rows,
            grid_cols=grid_cols,
            col_weights=tuple([1] * grid_cols),
            row_weights=tuple([1] * grid_rows),
            col_locked=tuple([False] * grid_cols),
            row_locked=tuple([False] * grid_rows),
            canvas_size=canvas_size,
            created_at=ts,
            updated_at=ts,
        )

    # Functional "setters" — preserve immutability (R-07-style discipline) -

    def with_dimensions(
        self,
        grid_rows: int,
        grid_cols: int,
        col_weights: Sequence[int],
        row_weights: Sequence[int],
        col_locked: Sequence[bool],
        row_locked: Sequence[bool],
        *,
        now: datetime | None = None,
    ) -> "GridCanvas":
        """Return a copy with new dimensions + matching weight/lock arrays.

        The caller (UC-02 use case) is responsible for arranging the weight
        and lock arrays via the Fit algorithm (workflow_decision). This
        Entity method only validates the resulting shape via __post_init__.
        """

        return replace(
            self,
            grid_rows=grid_rows,
            grid_cols=grid_cols,
            col_weights=tuple(col_weights),
            row_weights=tuple(row_weights),
            col_locked=tuple(col_locked),
            row_locked=tuple(row_locked),
            updated_at=now or _now(),
        )

    def with_weights(
        self,
        axis: "Axis",
        weights: Sequence[int],
        *,
        now: datetime | None = None,
    ) -> "GridCanvas":
        ts = now or _now()
        if axis is Axis.COL:
            return replace(self, col_weights=tuple(weights), updated_at=ts)
        return replace(self, row_weights=tuple(weights), updated_at=ts)

    def with_lock_toggled(
        self,
        axis: "Axis",
        index: int,
        *,
        now: datetime | None = None,
    ) -> "GridCanvas":
        ts = now or _now()
        if axis is Axis.COL:
            new = list(self.col_locked)
            new[index] = not new[index]
            return replace(self, col_locked=tuple(new), updated_at=ts)
        new = list(self.row_locked)
        new[index] = not new[index]
        return replace(self, row_locked=tuple(new), updated_at=ts)


@dataclass(frozen=True, slots=True)
class Placement:
    """A single image-copy placement on a GridCanvas.

    R-07: position and occupy_size are immutable within one Placement
    instance. Use cases that "move" or "resize" produce a new Placement
    value (via `with_position` / `with_occupy_size`) which the use case then
    persists, replacing the previous record by id. The before/after
    snapshots remain independently observable (see test_immutability.py).

    placement_order participates in R-06 but is owned at the set level, not
    the entity level — see rules.orders_are_unique.
    """

    id: UUID
    grid_id: UUID
    copy_id: UUID
    position: CellPosition
    occupy_size: OccupySize
    placement_order: int
    created_at: datetime

    def __post_init__(self) -> None:
        if self.placement_order < 1:
            raise ValueError(
                f"placement_order must be >= 1 (got {self.placement_order})"
            )

    # Constructor ---------------------------------------------------------

    @classmethod
    def create(
        cls,
        grid_id: UUID,
        copy_id: UUID,
        position: CellPosition,
        occupy_size: OccupySize,
        placement_order: int,
        *,
        id: UUID | None = None,
        now: datetime | None = None,
    ) -> "Placement":
        return cls(
            id=id or _new_id(),
            grid_id=grid_id,
            copy_id=copy_id,
            position=position,
            occupy_size=occupy_size,
            placement_order=placement_order,
            created_at=now or _now(),
        )

    # Functional setters --------------------------------------------------

    def with_position(self, position: CellPosition) -> "Placement":
        return replace(self, position=position)

    def with_occupy_size(self, occupy_size: OccupySize) -> "Placement":
        return replace(self, occupy_size=occupy_size)

    def with_placement_order(self, placement_order: int) -> "Placement":
        return replace(self, placement_order=placement_order)


# -- Misc -------------------------------------------------------------------


from enum import Enum  # placed after Placement so __post_init__ refs resolve


class Axis(Enum):
    """Identifies a row-axis or col-axis operation. Used by UC-03 and UC-04
    instead of bare strings to keep failure reasons stable."""

    ROW = "row"
    COL = "col"


class OrderOperation(Enum):
    """UC-09 operation kinds, mirrored from 21-grid-composition.yaml."""

    BringToFront = "BringToFront"
    SendToBack = "SendToBack"
    MoveForward = "MoveForward"
    MoveBackward = "MoveBackward"
    SetOrder = "SetOrder"
