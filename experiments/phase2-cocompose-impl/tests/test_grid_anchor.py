"""GRID Anchor tests AT-01..AT-10 (30-design.md §8).

Implemented verbatim from the worked examples; all must pass. AT-07 is the
1000-step random walk (seed-fixed, reproducible) that detects the W-3 swap bug.
"""

import random
import uuid

import grid_composition.events as ev
from grid_composition.domain import Axis, CellPosition, OrderOperation
from grid_composition.domain import find_overlaps, fits_within_grid
from grid_composition.failures import (
    Conflict,
    InvalidIndex,
    NotFound,
    WouldOrphanPlacements,
)
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def _new_grid(g, rows=3, cols=3):
    return g.create_grid_canvas("g", rows, cols, PixelSize(cols * 100, rows * 100)).unwrap()


def _place(g, gid, x, y, w=1, h=1):
    return g.place_image_copy(gid, uuid.uuid4(), CellPosition(x, y), OccupySize(w, h))


# AT-01 (W-1): empty grid first placement -> order=1, one PlacementCreated.
def test_at_01_first_placement_order_one(grid_always, bus):
    gid = _new_grid(grid_always)
    pid = _place(grid_always, gid, 0, 0).unwrap()
    p = grid_always.list_placements(gid)[0]
    assert p.id == pid and p.placement_order == 1
    assert len(bus.of_type(ev.PlacementCreated)) == 1


# AT-02 (W-2): same-position move excludes self -> no conflict.
def test_at_02_same_position_move_no_self_conflict(grid_always):
    gid = _new_grid(grid_always)
    a = _place(grid_always, gid, 0, 0, 2, 1).unwrap()
    _place(grid_always, gid, 2, 0, 1, 1)
    res = grid_always.move_placement(a, CellPosition(0, 0))
    assert isinstance(res, Ok)


# AT-03 (W-3): asymmetric-size swap mutual collision -> Conflict([A,B]).
def test_at_03_asymmetric_swap_conflict(grid_always):
    gid = _new_grid(grid_always)
    a = _place(grid_always, gid, 0, 0, 1, 1).unwrap()  # A: 1x1 at (0,0)
    b = _place(grid_always, gid, 1, 0, 2, 1).unwrap()  # B: 2x1 at (1,0)
    res = grid_always.swap_placements(a, b)
    assert isinstance(res, Err) and isinstance(res.error, Conflict)
    assert set(res.error.conflicting_placement_ids) == {a, b}
    # state unchanged, no event
    assert len(bus_events_swapped(grid_always)) == 0
    pa = grid_always.list_placements(gid)
    positions = {p.id: (p.position.x, p.position.y) for p in pa}
    assert positions[a] == (0, 0) and positions[b] == (1, 0)


def bus_events_swapped(g):
    # helper: access the bus through the use cases object
    return g._bus.of_type(ev.PlacementsSwapped)


# AT-04 (W-4): SetOrder pushes others down.
def test_at_04_set_order_pushes_others(grid_always):
    gid = _new_grid(grid_always)
    p1 = _place(grid_always, gid, 0, 0).unwrap()
    p2 = _place(grid_always, gid, 1, 0).unwrap()
    p3 = _place(grid_always, gid, 2, 0).unwrap()
    res = grid_always.change_placement_order(p3, OrderOperation.SetOrder, 1)
    assert isinstance(res, Ok)
    orders = {p.id: p.placement_order for p in grid_always.list_placements(gid)}
    assert orders[p3] == 1 and orders[p1] == 2 and orders[p2] == 3


# AT-05 (W-5): remove compacts orders.
def test_at_05_remove_compacts_order(grid_always):
    gid = _new_grid(grid_always)
    p1 = _place(grid_always, gid, 0, 0).unwrap()
    p2 = _place(grid_always, gid, 1, 0).unwrap()
    p3 = _place(grid_always, gid, 2, 0).unwrap()
    p4 = _place(grid_always, gid, 0, 1).unwrap()
    grid_always.remove_placement(p2)
    orders = {p.id: p.placement_order for p in grid_always.list_placements(gid)}
    assert orders == {p1: 1, p3: 2, p4: 3}


# AT-06 (W-6): NotFound payload carries entity_kind.
def test_at_06_not_found_payload(grid_always):
    missing = uuid.uuid4()
    res = grid_always.move_placement(missing, CellPosition(0, 0))
    assert isinstance(res.error, NotFound)
    assert res.error.entity_kind == "Placement"
    assert res.error.entity_id == missing


# AT-07: 1000-step random walk; R-01, R-02, R-06 always hold.
def test_at_07_random_walk_invariants(grid_always):
    rng = random.Random(20260529)  # fixed seed -> reproducible
    gids = [_new_grid(grid_always, rng.randint(2, 5), rng.randint(2, 5))
            for _ in range(3)]

    def check_invariants():
        for gid in gids:
            grid_obj = grid_always._grids.get_by_id(gid)
            placements = grid_always.list_placements(gid)
            # R-01
            for p in placements:
                assert fits_within_grid(
                    p.position, p.occupy_size, grid_obj.grid_rows, grid_obj.grid_cols)
            # R-02
            for i, p in enumerate(placements):
                others = placements[:i] + placements[i + 1:]
                assert find_overlaps(p.position, p.occupy_size, others) == []
            # R-06: orders unique and dense 1..N
            orders = sorted(p.placement_order for p in placements)
            assert orders == list(range(1, len(placements) + 1))

    for _ in range(1000):
        gid = rng.choice(gids)
        grid_obj = grid_always._grids.get_by_id(gid)
        placements = grid_always.list_placements(gid)
        op = rng.randint(0, 5)
        if op == 0:  # place
            x = rng.randint(0, grid_obj.grid_cols - 1)
            y = rng.randint(0, grid_obj.grid_rows - 1)
            w = rng.randint(1, max(1, grid_obj.grid_cols - x))
            h = rng.randint(1, max(1, grid_obj.grid_rows - y))
            _place(grid_always, gid, x, y, w, h)
        elif op == 1 and placements:  # move
            p = rng.choice(placements)
            x = rng.randint(0, grid_obj.grid_cols - 1)
            y = rng.randint(0, grid_obj.grid_rows - 1)
            grid_always.move_placement(p.id, CellPosition(x, y))
        elif op == 2 and len(placements) >= 2:  # swap
            a, b = rng.sample(placements, 2)
            grid_always.swap_placements(a.id, b.id)
        elif op == 3 and placements:  # resize
            p = rng.choice(placements)
            w = rng.randint(1, grid_obj.grid_cols)
            h = rng.randint(1, grid_obj.grid_rows)
            grid_always.resize_placement_occupancy(p.id, OccupySize(w, h))
        elif op == 4 and placements:  # reorder
            p = rng.choice(placements)
            grid_always.change_placement_order(
                p.id, rng.choice(list(OrderOperation)[:4]))
        elif op == 5 and placements:  # remove
            p = rng.choice(placements)
            grid_always.remove_placement(p.id)
        check_invariants()


# AT-08: after any op sequence, ListPlacements is z-order ascending.
def test_at_08_list_is_z_order_ascending(grid_always):
    rng = random.Random(7)
    gid = _new_grid(grid_always, 4, 4)
    for _ in range(50):
        x, y = rng.randint(0, 3), rng.randint(0, 3)
        _place(grid_always, gid, x, y)
        placements = grid_always.list_placements(gid)
        orders = [p.placement_order for p in placements]
        assert orders == sorted(orders)


# AT-09: shrink that orphans -> WouldOrphanPlacements.
def test_at_09_shrink_orphans(grid_always):
    gid = _new_grid(grid_always, 3, 3)
    _place(grid_always, gid, 2, 2)
    res = grid_always.change_grid_dimensions(gid, 2, 2)
    assert isinstance(res.error, WouldOrphanPlacements)


# AT-10: out-of-range lock index -> InvalidIndex.
def test_at_10_lock_index_out_of_range(grid_always):
    gid = _new_grid(grid_always, 3, 3)
    res = grid_always.toggle_row_column_lock(gid, Axis.Col, 5)
    assert isinstance(res.error, InvalidIndex)
