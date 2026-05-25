"""Anchor tests AT-01 .. AT-10 (30-design.md §8).

Per 30-design.md §8 these are the reference tests; AI may not weaken the
expected behaviour. Test names start with ``test_at_NN_`` to be greppable.
"""

from __future__ import annotations

import random

import pytest
from hypothesis import HealthCheck, given, settings, strategies as st

from grid_composition import (
    CellPosition,
    Conflict,
    GridCompositionUseCases,
    InMemoryGridCanvasRepository,
    InMemoryImageCopyExistenceCheck,
    InMemoryPlacementRepository,
    InvalidIndex,
    NotFound,
    OccupySize,
    OrderOperation,
    PixelSize,
    PlacementCreated,
    PlacementMoved,
    RecordingBus,
    WouldOrphanPlacements,
    Axis,
    new_id,
)


# AT-01 — W-1: Empty grid + first placement -> order=1
def test_at_01_empty_grid_first_placement_order_is_one(uc, small_grid, a_copy_id, bus):
    res = uc.place_image_copy(
        grid_id=small_grid.id,
        copy_id=a_copy_id,
        position=CellPosition(0, 0),
        occupy_size=OccupySize(1, 1),
    )
    assert res.is_ok
    assert res.value.placement_order == 1
    assert sum(1 for e in bus.events if isinstance(e, PlacementCreated)) == 1


# AT-02 — W-2: Same-position Move excludes self from collision check
def test_at_02_move_to_same_position_does_not_self_conflict(uc, small_grid, a_copy_id):
    a = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(2, 1),
    ).value
    uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(2, 0), occupy_size=OccupySize(1, 1),
    )
    # Move A to its own position
    res = uc.move_placement(placement_id=a.id, new_position=CellPosition(0, 0))
    assert res.is_ok, f"expected success, got {res}"


# AT-03 — W-3: A/B asymmetric size swap must yield Conflict (post-swap intersection)
def test_at_03_swap_asymmetric_sizes_conflict(uc, small_grid, a_copy_id):
    a = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    ).value
    b = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(1, 0), occupy_size=OccupySize(2, 1),
    ).value
    res = uc.swap_placements(placement_id_a=a.id, placement_id_b=b.id)
    assert res.is_err
    assert isinstance(res.failure, Conflict)
    assert set(res.failure.conflicting_placement_ids) == {a.id, b.id}


# AT-04 — W-4: SetOrder pushes others down
def test_at_04_set_order_pushes_others_down(uc, small_grid, a_copy_id, placements):
    p1 = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    ).value
    p2 = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(1, 0), occupy_size=OccupySize(1, 1),
    ).value
    p3 = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(2, 0), occupy_size=OccupySize(1, 1),
    ).value
    res = uc.change_placement_order(
        placement_id=p3.id, operation=OrderOperation.SET_ORDER, order_value=1
    )
    assert res.is_ok
    actual = {p.id: p.placement_order for p in placements.get_by_grid(small_grid.id)}
    assert actual[p3.id] == 1
    assert actual[p1.id] == 2
    assert actual[p2.id] == 3


# AT-05 — W-5: Remove + order compaction
def test_at_05_remove_compacts_orders(uc, small_grid, a_copy_id, placements):
    ps = []
    for x in (0, 1, 2):
        ps.append(
            uc.place_image_copy(
                grid_id=small_grid.id, copy_id=a_copy_id,
                position=CellPosition(x, 0), occupy_size=OccupySize(1, 1),
            ).value
        )
    # Three more in the next row to push order count to 4? we have 3; add one more
    ps.append(
        uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 1), occupy_size=OccupySize(1, 1),
        ).value
    )
    # Now ps[0..3] have orders 1..4. Remove ps[1] (order=2).
    res = uc.remove_placement(placement_id=ps[1].id)
    assert res.is_ok
    actual = {p.id: p.placement_order for p in placements.get_by_grid(small_grid.id)}
    # P1=1, P3=2, P4=3 (1..N continuous)
    assert actual[ps[0].id] == 1
    assert actual[ps[2].id] == 2
    assert actual[ps[3].id] == 3


# AT-06 — W-6: NotFound carries entity_kind
def test_at_06_not_found_carries_entity_kind(uc):
    bogus = new_id()
    res = uc.move_placement(placement_id=bogus, new_position=CellPosition(0, 0))
    assert res.is_err
    assert isinstance(res.failure, NotFound)
    assert res.failure.entity_kind == "Placement"
    assert res.failure.entity_id == bogus


# AT-07 — Property: 1000-step random walk preserves R-01, R-02, R-06
@settings(
    max_examples=1,  # ONE example, but each one executes 1000 internal steps
    deadline=None,
    suppress_health_check=[HealthCheck.too_slow, HealthCheck.function_scoped_fixture],
    derandomize=True,  # seed-fixed reproducibility per 30-design §6.3
)
@given(seed=st.integers(min_value=0, max_value=10**9))
def test_at_07_random_walk_preserves_invariants(seed):
    """1000-step random walk: invariants R-01, R-02, R-06 must hold after
    every operation regardless of failure or success."""
    _run_random_walk(seed=seed, n_steps=1000)


# AT-08 — Property: after any op, UC-11 result is ascending z-order
@settings(
    max_examples=10,
    deadline=None,
    suppress_health_check=[HealthCheck.too_slow, HealthCheck.function_scoped_fixture],
    derandomize=True,
)
@given(seed=st.integers(min_value=0, max_value=10**6))
def test_at_08_listed_placements_ascending_z_order(seed):
    grids = InMemoryGridCanvasRepository()
    placements = InMemoryPlacementRepository()
    copies = InMemoryImageCopyExistenceCheck()
    bus = RecordingBus()
    uc = GridCompositionUseCases(grids, placements, copies, bus)
    grid_id, copy_id = _bootstrap(uc, copies, rows=3, cols=3)
    rnd = random.Random(seed)
    for _ in range(100):
        _random_op(uc, grid_id, copy_id, rnd, placements)
        listing = uc.list_placements(grid_id=grid_id).value
        orders = [p.placement_order for p in listing]
        assert orders == sorted(orders)


# AT-09 — UC-02 dimension shrink with out-of-bounds placement -> WouldOrphanPlacements
def test_at_09_dimension_shrink_orphans(uc, small_grid, a_copy_id):
    uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(2, 2), occupy_size=OccupySize(1, 1),
    )
    res = uc.change_grid_dimensions(
        grid_id=small_grid.id, new_grid_rows=2, new_grid_cols=2
    )
    assert res.is_err
    assert isinstance(res.failure, WouldOrphanPlacements)


# AT-10 — UC-04 invalid index -> InvalidIndex
def test_at_10_invalid_lock_index(uc, small_grid):
    res = uc.toggle_row_column_lock(grid_id=small_grid.id, axis=Axis.COL, index=99)
    assert res.is_err
    assert isinstance(res.failure, InvalidIndex)


# ---------------------------- helpers -------------------------------------

def _bootstrap(uc, copies, *, rows: int, cols: int):
    g = uc.create_grid_canvas(
        name="rw", grid_rows=rows, grid_cols=cols,
        canvas_size=PixelSize(width=cols * 10, height=rows * 10),
    ).value
    cid = new_id()
    copies.add(cid)
    return g.id, cid


def _random_op(uc, grid_id, copy_id, rnd: random.Random, placements):
    """Issue one random UseCase invocation. We don't care if it fails — the
    point is that invariants hold afterwards regardless."""
    op = rnd.choice([
        "place", "move", "swap", "resize", "order", "remove", "list",
        "weights", "lock",
    ])
    existing = placements.get_by_grid(grid_id)
    if op == "place":
        uc.place_image_copy(
            grid_id=grid_id, copy_id=copy_id,
            position=CellPosition(rnd.randint(0, 2), rnd.randint(0, 2)),
            occupy_size=OccupySize(rnd.randint(1, 2), rnd.randint(1, 2)),
        )
    elif op == "move" and existing:
        p = rnd.choice(existing)
        uc.move_placement(
            placement_id=p.id,
            new_position=CellPosition(rnd.randint(0, 2), rnd.randint(0, 2)),
        )
    elif op == "swap" and len(existing) >= 2:
        a, b = rnd.sample(existing, 2)
        uc.swap_placements(placement_id_a=a.id, placement_id_b=b.id)
    elif op == "resize" and existing:
        p = rnd.choice(existing)
        uc.resize_placement_occupancy(
            placement_id=p.id,
            new_occupy_size=OccupySize(rnd.randint(1, 2), rnd.randint(1, 2)),
        )
    elif op == "order" and existing:
        p = rnd.choice(existing)
        operation = rnd.choice(list(OrderOperation))
        if operation == OrderOperation.SET_ORDER:
            n = len(existing)
            order_value = rnd.randint(1, n)
            uc.change_placement_order(
                placement_id=p.id, operation=operation, order_value=order_value
            )
        else:
            uc.change_placement_order(placement_id=p.id, operation=operation)
    elif op == "remove" and existing:
        p = rnd.choice(existing)
        uc.remove_placement(placement_id=p.id)
    elif op == "list":
        uc.list_placements(grid_id=grid_id)
    elif op == "weights":
        # Pick row or col
        axis = rnd.choice([Axis.ROW, Axis.COL])
        uc.change_row_column_weights(grid_id=grid_id, axis=axis, weights=[1, 2, 1])
    elif op == "lock":
        axis = rnd.choice([Axis.ROW, Axis.COL])
        uc.toggle_row_column_lock(grid_id=grid_id, axis=axis, index=rnd.randint(0, 2))


def _check_invariants(uc, grid_id, placements):
    """Verify R-01, R-02, R-06 hold for current state."""
    grid = uc._grids.get_by_id(grid_id)  # internal but acceptable for invariant probe
    ps = placements.get_by_grid(grid_id)
    # R-01: every placement fits within grid
    for p in ps:
        assert (
            p.position.x >= 0
            and p.position.y >= 0
            and p.position.x + p.occupy_size.width <= grid.grid_cols
            and p.position.y + p.occupy_size.height <= grid.grid_rows
        ), f"R-01 broken: placement {p}"
    # R-02: no pair overlaps
    cell_sets = [(p.id, p.occupied_cells()) for p in ps]
    for i in range(len(cell_sets)):
        for j in range(i + 1, len(cell_sets)):
            assert not (cell_sets[i][1] & cell_sets[j][1]), (
                f"R-02 broken: {cell_sets[i][0]} ∩ {cell_sets[j][0]} != ∅"
            )
    # R-06: orders are unique and dense 1..N
    orders = sorted(p.placement_order for p in ps)
    assert orders == list(range(1, len(orders) + 1)), f"R-06/R-09 broken: orders={orders}"


def _run_random_walk(*, seed: int, n_steps: int):
    grids = InMemoryGridCanvasRepository()
    placements = InMemoryPlacementRepository()
    copies = InMemoryImageCopyExistenceCheck()
    bus = RecordingBus()
    uc = GridCompositionUseCases(grids, placements, copies, bus)
    grid_id, copy_id = _bootstrap(uc, copies, rows=3, cols=3)

    rnd = random.Random(seed)
    for _ in range(n_steps):
        _random_op(uc, grid_id, copy_id, rnd, placements)
        _check_invariants(uc, grid_id, placements)
