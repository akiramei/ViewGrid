"""GRID_COMPOSITION domain model + pure-function rules.

Entities (21-grid-composition.yaml §entities):
  - GridCanvas (R-03, R-04, R-05 enforced at construction)
  - Placement  (R-01, R-02, R-06, R-07 enforced as a set by the UseCase layer)

Value objects:
  - CellPosition (local to GRID)
  - OccupySize / PixelSize -> imported from src/shared (C-SHARED-PLACEMENT)

Enums (C-ENUM = enum.Enum):
  - Axis, OrderOperation

Rules R-01/R-02 are pure functions here so the UseCase layer can call them in a
single place each (audit: one enforcement site per Rule).
"""

from __future__ import annotations

import enum
import uuid
from dataclasses import dataclass, replace
from datetime import datetime, timezone

# C-SHARED-PLACEMENT: import the single shared definition (do NOT redefine).
from shared.value_objects import OccupySize, PixelSize


def _utc_now() -> datetime:
    # C-TIMESTAMP: UTC, tz-aware.
    return datetime.now(timezone.utc)


class Axis(enum.Enum):
    Row = "Row"
    Col = "Col"


class OrderOperation(enum.Enum):
    BringToFront = "BringToFront"
    SendToBack = "SendToBack"
    MoveForward = "MoveForward"
    MoveBackward = "MoveBackward"
    SetOrder = "SetOrder"


@dataclass(frozen=True)
class CellPosition:
    """0-based cell coordinate. x = column, y = row."""

    x: int
    y: int

    def __post_init__(self) -> None:
        if isinstance(self.x, bool) or isinstance(self.y, bool):
            raise TypeError("CellPosition coordinates must be int, not bool")
        if not isinstance(self.x, int) or not isinstance(self.y, int):
            raise TypeError("CellPosition coordinates must be int")


@dataclass(frozen=True)
class GridCanvas:
    """Grid canvas entity. R-03/R-04/R-05 enforced in __post_init__."""

    id: uuid.UUID
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
        # R-03: GridDimensionsMustBePositive
        if isinstance(self.grid_rows, bool) or isinstance(self.grid_cols, bool):
            raise TypeError("grid_rows / grid_cols must be int, not bool")
        if not isinstance(self.grid_rows, int) or not isinstance(self.grid_cols, int):
            raise TypeError("grid_rows / grid_cols must be int")
        if self.grid_rows < 1 or self.grid_cols < 1:
            raise ValueError("grid_rows and grid_cols must be >= 1 (R-03)")
        # canvas_size positivity already guaranteed by PixelSize (>= 1).

        # R-04: WeightsMustBePositiveIntegers
        for label, weights in (("col_weights", self.col_weights),
                               ("row_weights", self.row_weights)):
            for w in weights:
                if isinstance(w, bool) or not isinstance(w, int) or w < 1:
                    raise ValueError(f"{label} must all be positive ints (R-04)")

        # R-05: WeightArrayLengthMatchesDimension
        if len(self.col_weights) != self.grid_cols:
            raise ValueError("len(col_weights) must equal grid_cols (R-05)")
        if len(self.row_weights) != self.grid_rows:
            raise ValueError("len(row_weights) must equal grid_rows (R-05)")
        if len(self.col_locked) != self.grid_cols:
            raise ValueError("len(col_locked) must equal grid_cols")
        if len(self.row_locked) != self.grid_rows:
            raise ValueError("len(row_locked) must equal grid_rows")

    def snapshot(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "grid_rows": self.grid_rows,
            "grid_cols": self.grid_cols,
            "col_weights": self.col_weights,
            "row_weights": self.row_weights,
            "col_locked": self.col_locked,
            "row_locked": self.row_locked,
            "canvas_size": self.canvas_size,
        }

    @staticmethod
    def create(name: str, grid_rows: int, grid_cols: int,
               canvas_size: PixelSize) -> "GridCanvas":
        """UC-01 factory. Uniform weights, all unlocked."""
        now = _utc_now()
        return GridCanvas(
            id=uuid.uuid4(),  # C-IDENTITY: uuid4 directly, never str
            name=name,
            grid_rows=grid_rows,
            grid_cols=grid_cols,
            col_weights=tuple(1 for _ in range(grid_cols)),
            row_weights=tuple(1 for _ in range(grid_rows)),
            col_locked=tuple(False for _ in range(grid_cols)),
            row_locked=tuple(False for _ in range(grid_rows)),
            canvas_size=canvas_size,
            created_at=now,
            updated_at=now,
        )


@dataclass(frozen=True)
class Placement:
    """Placement entity. position / occupy_size logically immutable (R-07):
    mutation is expressed by replacing the value (see `with_*`)."""

    id: uuid.UUID
    grid_id: uuid.UUID
    copy_id: uuid.UUID
    position: CellPosition
    occupy_size: OccupySize
    placement_order: int
    created_at: datetime

    def snapshot(self) -> dict:
        return {
            "id": self.id,
            "grid_id": self.grid_id,
            "copy_id": self.copy_id,
            "position": self.position,
            "occupy_size": self.occupy_size,
            "placement_order": self.placement_order,
        }

    def with_position(self, position: CellPosition) -> "Placement":
        return replace(self, position=position)

    def with_occupy_size(self, occupy_size: OccupySize) -> "Placement":
        return replace(self, occupy_size=occupy_size)

    def with_order(self, order: int) -> "Placement":
        return replace(self, placement_order=order)


# ----------------------------------------------------------------------------
# Pure-function rules (R-01, R-02). One enforcement site each.
# ----------------------------------------------------------------------------

def occupied_cells(position: CellPosition, size: OccupySize) -> frozenset[tuple[int, int]]:
    """Cells covered by an occupy rectangle whose top-left is `position`."""
    return frozenset(
        (position.x + dx, position.y + dy)
        for dx in range(size.width)
        for dy in range(size.height)
    )


def fits_within_grid(position: CellPosition, size: OccupySize,
                     grid_rows: int, grid_cols: int) -> bool:
    """R-01: PlacementMustFitWithinGrid (pure function)."""
    return (
        position.x >= 0
        and position.y >= 0
        and position.x + size.width <= grid_cols
        and position.y + size.height <= grid_rows
    )


def find_overlaps(position: CellPosition, size: OccupySize,
                  others: list[Placement]) -> list[uuid.UUID]:
    """R-02: PlacementsMustNotOverlap (pure function).

    Returns the ids of `others` whose occupied cells intersect the candidate.
    Callers pass `others` already filtered to exclude the subject(s) per the
    Rule ledger (UC-06 excludes self; UC-07 excludes both; UC-08 excludes self;
    UC-02 excludes nothing).
    """
    candidate = occupied_cells(position, size)
    conflicts: list[uuid.UUID] = []
    for other in others:
        if occupied_cells(other.position, other.occupy_size) & candidate:
            conflicts.append(other.id)
    return conflicts
