"""Rule predicates (pure functions).

These functions are the SOLE authority for the Rules they implement, per
21-grid-composition.yaml §rules `enforced_at: use_case_layer`.

Mapping:
- R-01 PlacementMustFitWithinGrid       -> placement_fits_within_grid
- R-02 PlacementsMustNotOverlap         -> placement_overlaps
- R-06 PlacementOrderMustBeUnique       -> orders_are_unique
- R-09 RemovedPlacementOrderMustBeCompacted -> compact_orders

R-03, R-04, R-05, R-07 are owned by the domain layer (Entity constructors /
immutability). R-08 lives inside the UC-02 use case as a workflow_decision
(Fit algorithm), so it is exported from use_cases.change_grid_dimensions
rather than this module — the design doc 30-design.md §1 R-08 explicitly
says "Owned by: UseCase 層 (Fit 動作)". We keep this module to the
predicates that judge a candidate placement against a grid state.

All functions are PURE: no I/O, no events, no mutation. This is required by
30-design.md §2.1 ("これらはすべて純粋関数として実装可能でなければならない").
"""

from __future__ import annotations

from typing import Iterable
from uuid import UUID

from .domain import CellPosition, GridCanvas, OccupySize, Placement


# -- R-01: PlacementMustFitWithinGrid ---------------------------------------


def placement_fits_within_grid(
    position: CellPosition,
    occupy_size: OccupySize,
    grid_rows: int,
    grid_cols: int,
) -> bool:
    """R-01. True iff the rectangle [x, x+w) x [y, y+h) fits within the grid.

    Per 30-design.md §1 R-01:
        x >= 0; y >= 0; x + w <= C; y + h <= R.
    """

    return (
        position.x >= 0
        and position.y >= 0
        and position.x + occupy_size.width <= grid_cols
        and position.y + occupy_size.height <= grid_rows
    )


# -- R-02: PlacementsMustNotOverlap -----------------------------------------


def _occupied_cells(position: CellPosition, occupy_size: OccupySize) -> set[tuple[int, int]]:
    """Set of (x, y) cells the rectangle covers."""

    return {
        (position.x + dx, position.y + dy)
        for dx in range(occupy_size.width)
        for dy in range(occupy_size.height)
    }


def placement_overlaps(
    candidate_position: CellPosition,
    candidate_size: OccupySize,
    existing: Iterable[Placement],
    *,
    exclude_ids: Iterable[UUID] = (),
) -> tuple[UUID, ...]:
    """R-02. Return the placement_ids of existing placements whose occupied
    cells intersect the candidate's. Empty tuple => no overlap.

    `exclude_ids` is the design.md R-02 "除外対象" set: in UC-06 the moving
    placement excludes itself; in UC-07 both swap partners exclude each
    other; in UC-08 the resized placement excludes itself; in UC-02 the set
    is empty.
    """

    excluded = set(exclude_ids)
    candidate_cells = _occupied_cells(candidate_position, candidate_size)
    conflicts: list[UUID] = []
    for p in existing:
        if p.id in excluded:
            continue
        if _occupied_cells(p.position, p.occupy_size) & candidate_cells:
            conflicts.append(p.id)
    return tuple(conflicts)


# -- R-06: PlacementOrderMustBeUnique ---------------------------------------


def orders_are_unique(placements: Iterable[Placement]) -> bool:
    """R-06. True iff every placement_order in the set is distinct."""

    seen: set[int] = set()
    for p in placements:
        if p.placement_order in seen:
            return False
        seen.add(p.placement_order)
    return True


# -- R-09: RemovedPlacementOrderMustBeCompacted -----------------------------


def compact_orders(placements: list[Placement]) -> tuple[list[Placement], dict[UUID, int]]:
    """R-09. Renumber the given placements so their orders form 1..N (dense)
    while preserving the existing relative ranking. Returns the new list and
    a mapping {placement_id -> new_order} for the PlacementRemoved event
    payload.

    Stable: ties on placement_order are broken by created_at, then by id.
    """

    sorted_p = sorted(
        placements,
        key=lambda p: (p.placement_order, p.created_at, str(p.id)),
    )
    new_list: list[Placement] = []
    mapping: dict[UUID, int] = {}
    for idx, p in enumerate(sorted_p, start=1):
        if p.placement_order != idx:
            renumbered = p.with_placement_order(idx)
            new_list.append(renumbered)
            mapping[p.id] = idx
        else:
            new_list.append(p)
            mapping[p.id] = idx
    return new_list, mapping
