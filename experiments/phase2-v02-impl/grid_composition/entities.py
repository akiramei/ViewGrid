"""Domain Model — owned entities.

The entities themselves carry R-03, R-04, R-05 (Domain Model invariants).
R-01, R-02, R-06 are enforced at the UseCase layer, not here, per the
Rule Ledger (30-design.md §1).

R-07 (CellPositionAndOccupySizeAreImmutableInOnePlacement) is satisfied by
``@dataclass(frozen=True)``: a "mutation" must be expressed as a new
``Placement`` value, so before/after snapshots remain independently
observable.
"""

from __future__ import annotations

from dataclasses import dataclass, field, replace
from datetime import datetime, timezone
from typing import Tuple

from .identity import Id
from .value_objects import CellPosition, OccupySize, PixelSize


def _utcnow() -> datetime:
    """Timestamp source. MUST_DECIDE_AND_DOCUMENT: we use UTC."""
    return datetime.now(timezone.utc)


@dataclass(frozen=True)
class GridCanvas:
    """Grid canvas. Owns R-03, R-04, R-05 (length-match part).

    All weight/lock arrays are stored as ``tuple`` to keep the entity
    structurally immutable (R-07-style observability also for canvas state).
    """

    id: Id
    name: str
    grid_rows: int
    grid_cols: int
    col_weights: Tuple[int, ...]
    row_weights: Tuple[int, ...]
    col_locked: Tuple[bool, ...]
    row_locked: Tuple[bool, ...]
    canvas_size: PixelSize
    created_at: datetime
    updated_at: datetime

    def __post_init__(self) -> None:
        # R-03 — dimensions must be positive
        if self.grid_rows < 1 or self.grid_cols < 1:
            raise ValueError(
                f"R-03 violation: grid_rows and grid_cols must be >= 1 "
                f"(got rows={self.grid_rows}, cols={self.grid_cols})"
            )

        # R-04 — weights are positive integers
        for w in self.col_weights:
            if not isinstance(w, int) or isinstance(w, bool) or w <= 0:
                raise ValueError(f"R-04 violation: col_weights must be positive ints, got {w!r}")
        for w in self.row_weights:
            if not isinstance(w, int) or isinstance(w, bool) or w <= 0:
                raise ValueError(f"R-04 violation: row_weights must be positive ints, got {w!r}")

        # R-05 — array lengths match dimensions
        if len(self.col_weights) != self.grid_cols:
            raise ValueError(
                f"R-05 violation: len(col_weights) ({len(self.col_weights)}) "
                f"!= grid_cols ({self.grid_cols})"
            )
        if len(self.row_weights) != self.grid_rows:
            raise ValueError(
                f"R-05 violation: len(row_weights) ({len(self.row_weights)}) "
                f"!= grid_rows ({self.grid_rows})"
            )
        if len(self.col_locked) != self.grid_cols:
            raise ValueError(
                f"R-05 violation: len(col_locked) ({len(self.col_locked)}) "
                f"!= grid_cols ({self.grid_cols})"
            )
        if len(self.row_locked) != self.grid_rows:
            raise ValueError(
                f"R-05 violation: len(row_locked) ({len(self.row_locked)}) "
                f"!= grid_rows ({self.grid_rows})"
            )

    @staticmethod
    def create(
        *,
        id: Id,
        name: str,
        grid_rows: int,
        grid_cols: int,
        canvas_size: PixelSize,
    ) -> "GridCanvas":
        """Factory for UC-01 — uniform weight 1 and all-unlocked rows/cols."""
        now = _utcnow()
        return GridCanvas(
            id=id,
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

    def with_updated_at(self) -> "GridCanvas":
        return replace(self, updated_at=_utcnow())


@dataclass(frozen=True)
class Placement:
    """A placement: an ImageCopy positioned on a grid.

    R-07: ``position`` and ``occupy_size`` are immutable for a given
    placement value. The UseCase layer expresses "change" by producing a
    new ``Placement`` instance (see :func:`replace`).
    """

    id: Id
    grid_id: Id
    copy_id: Id
    position: CellPosition
    occupy_size: OccupySize
    placement_order: int
    created_at: datetime = field(default_factory=_utcnow)

    def __post_init__(self) -> None:
        if not isinstance(self.placement_order, int) or isinstance(self.placement_order, bool):
            raise TypeError("placement_order must be int")
        if self.placement_order < 1:
            raise ValueError(
                f"placement_order must be >= 1 (got {self.placement_order})"
            )

    def occupied_cells(self) -> frozenset:
        """Return the set of (x,y) cells this placement occupies."""
        x0, y0 = self.position.x, self.position.y
        w, h = self.occupy_size.width, self.occupy_size.height
        return frozenset((x0 + dx, y0 + dy) for dx in range(w) for dy in range(h))
