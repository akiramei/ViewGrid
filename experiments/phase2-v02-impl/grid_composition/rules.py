"""Use-case-layer Rule predicates (pure functions).

This module owns the SINGLE place where R-01 and R-02 are decided
(invariant predicates). R-03/R-04/R-05 belong to the domain model
(see ``entities.GridCanvas`` __post_init__).

R-06 (uniqueness) and R-09 (order compaction) are workflow concerns and
live alongside the UseCases that own them (UC-05, UC-09, UC-10).

R-02 has a SECOND check inside UC-07 (post-swap intersection check).
Per 30-design §1 R-02 NOTE and 40-prompt POST_IMPLEMENTATION_SELF_AUDIT
exception, that second check is classified as ``workflow_decision`` of
UC-07, NOT as a duplicate of R-02. It does not reuse this module's
predicate — see ``use_cases.py``.
"""

from __future__ import annotations

from typing import FrozenSet, Iterable, Tuple

from .entities import GridCanvas, Placement
from .identity import Id
from .value_objects import CellPosition, OccupySize


# ---------------------------------------------------------------------------
# R-01: PlacementMustFitWithinGrid (pure predicate)
# ---------------------------------------------------------------------------

def fits_within_grid(
    grid: GridCanvas, position: CellPosition, occupy_size: OccupySize
) -> bool:
    """R-01 — does the occupied rectangle lie inside the grid?"""
    if position.x < 0 or position.y < 0:
        return False
    if position.x + occupy_size.width > grid.grid_cols:
        return False
    if position.y + occupy_size.height > grid.grid_rows:
        return False
    return True


# ---------------------------------------------------------------------------
# R-02: PlacementsMustNotOverlap (pure predicate)
# ---------------------------------------------------------------------------

def occupied_cells(position: CellPosition, occupy_size: OccupySize) -> FrozenSet[Tuple[int, int]]:
    """Cells covered by a (position, size) pair."""
    return frozenset(
        (position.x + dx, position.y + dy)
        for dx in range(occupy_size.width)
        for dy in range(occupy_size.height)
    )


def find_conflicts(
    candidate_cells: FrozenSet[Tuple[int, int]],
    existing: Iterable[Placement],
    exclude_ids: Iterable[Id] = (),
) -> Tuple[Id, ...]:
    """R-02 — return ids of every placement whose occupied cells intersect
    ``candidate_cells``, skipping any whose id is in ``exclude_ids``.

    Returns the (possibly empty) tuple in deterministic order.
    """
    excluded = set(exclude_ids)
    hits: list[Id] = []
    for p in existing:
        if p.id in excluded:
            continue
        if candidate_cells & p.occupied_cells():
            hits.append(p.id)
    return tuple(hits)
