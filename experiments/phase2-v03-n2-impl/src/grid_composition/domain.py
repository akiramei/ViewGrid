"""GRID_COMPOSITION domain model + pure rule functions.

Entities:
  - GridCanvas (R-03, R-04, R-05 at construction)
  - Placement  (R-01, R-02, R-06, R-07 as a set; R-07 via immutability)

Pure rule helpers (R-01, R-02) are used by the UseCase layer.
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass, field, replace
from datetime import datetime, timezone

from shared.value_objects import OccupySize, PixelSize


def _utc_now() -> datetime:
    # C-TIMESTAMP: UTC, tz-aware.
    return datetime.now(timezone.utc)


@dataclass(frozen=True)
class CellPosition:
    """Cell coordinate (x = column, y = row)."""

    x: int
    y: int


def _validate_dimensions(grid_rows: int, grid_cols: int, canvas_size: PixelSize) -> None:
    # R-03: GridDimensionsMustBePositive.
    if isinstance(grid_rows, bool) or isinstance(grid_cols, bool):
        raise ValueError("grid dimensions must be ints, not bool")
    if not isinstance(grid_rows, int) or not isinstance(grid_cols, int):
        raise ValueError("grid dimensions must be ints")
    if grid_rows < 1 or grid_cols < 1:
        raise ValueError("grid_rows and grid_cols must be >= 1")
    if not isinstance(canvas_size, PixelSize):
        raise ValueError("canvas_size must be a PixelSize")


def _validate_weights(weights: tuple[int, ...], expected_len: int, axis: str) -> None:
    # R-04: WeightsMustBePositiveIntegers. R-05: length matches dimension.
    if len(weights) != expected_len:
        raise ValueError(f"{axis} weights length {len(weights)} != dimension {expected_len}")
    for w in weights:
        if isinstance(w, bool) or not isinstance(w, int):
            raise ValueError(f"{axis} weight must be a positive int")
        if w < 1:
            raise ValueError(f"{axis} weight must be >= 1")


@dataclass(frozen=True)
class GridCanvas:
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
        _validate_dimensions(self.grid_rows, self.grid_cols, self.canvas_size)
        _validate_weights(self.col_weights, self.grid_cols, "col")
        _validate_weights(self.row_weights, self.grid_rows, "row")
        if len(self.col_locked) != self.grid_cols:
            raise ValueError("col_locked length must match grid_cols")
        if len(self.row_locked) != self.grid_rows:
            raise ValueError("row_locked length must match grid_rows")

    @staticmethod
    def create(name: str, grid_rows: int, grid_cols: int, canvas_size: PixelSize) -> "GridCanvas":
        _validate_dimensions(grid_rows, grid_cols, canvas_size)
        now = _utc_now()
        return GridCanvas(
            id=uuid.uuid4(),
            name=name,
            grid_rows=grid_rows,
            grid_cols=grid_cols,
            col_weights=tuple([1] * grid_cols),
            row_weights=tuple([1] * grid_rows),
            col_locked=tuple([False] * grid_cols),
            row_locked=tuple([False] * grid_rows),
            canvas_size=canvas_size,
            created_at=now,
            updated_at=now,
        )

    def touched(self, **changes: object) -> "GridCanvas":
        return replace(self, updated_at=_utc_now(), **changes)


@dataclass(frozen=True)
class Placement:
    id: uuid.UUID
    grid_id: uuid.UUID
    copy_id: uuid.UUID
    position: CellPosition
    occupy_size: OccupySize
    placement_order: int
    created_at: datetime = field(default_factory=_utc_now)


# --------------------------------------------------------------------------
# Pure rule helpers (domain_decision; no I/O).
# --------------------------------------------------------------------------

def occupied_cells(position: CellPosition, size: OccupySize) -> set[tuple[int, int]]:
    return {
        (position.x + dx, position.y + dy)
        for dx in range(size.width)
        for dy in range(size.height)
    }


def fits_within_grid(
    position: CellPosition, size: OccupySize, grid_rows: int, grid_cols: int
) -> bool:
    # R-01: PlacementMustFitWithinGrid.
    return (
        position.x >= 0
        and position.y >= 0
        and position.x + size.width <= grid_cols
        and position.y + size.height <= grid_rows
    )


def find_conflicts(
    candidate_cells: set[tuple[int, int]],
    others: list[Placement],
    exclude_ids: set[uuid.UUID],
) -> list[uuid.UUID]:
    # R-02: PlacementsMustNotOverlap. Returns conflicting placement ids.
    conflicts: list[uuid.UUID] = []
    for other in others:
        if other.id in exclude_ids:
            continue
        if occupied_cells(other.position, other.occupy_size) & candidate_cells:
            conflicts.append(other.id)
    return conflicts
