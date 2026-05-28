"""GRID UseCase happy-path, failure-path and event-emission tests (UC-01..UC-11).

These use the grid_always fixture (Port that always returns True) so GRID can be
exercised in isolation; UnknownCopyId / compose wiring are covered separately.
"""

import uuid

import grid_composition.events as ev
from grid_composition.domain import Axis, CellPosition, OrderOperation
from grid_composition.failures import (
    Conflict,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    NotFound,
    OutOfBounds,
    UnknownCopyId,
    WouldOrphanPlacements,
)
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def _grid(grid_always):
    return grid_always.create_grid_canvas("g", 3, 3, PixelSize(300, 300)).unwrap()


def _place(grid_always, gid, x, y, w=1, h=1):
    return grid_always.place_image_copy(
        gid, uuid.uuid4(), CellPosition(x, y), OccupySize(w, h))


# --- UC-01 -------------------------------------------------------------
def test_uc01_happy(grid_always, bus):
    res = grid_always.create_grid_canvas("g", 2, 4, PixelSize(100, 200))
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.GridCanvasCreated)) == 1


def test_uc01_invalid_dimensions(grid_always):
    res = grid_always.create_grid_canvas("g", 0, 4, PixelSize(100, 200))
    assert isinstance(res, Err) and isinstance(res.error, InvalidDimensions)


# --- UC-05 -------------------------------------------------------------
def test_uc05_happy_order_one(grid_always, bus):
    gid = _grid(grid_always)
    res = _place(grid_always, gid, 0, 0)
    assert isinstance(res, Ok)
    placements = grid_always.list_placements(gid)
    assert placements[0].placement_order == 1
    assert len(bus.of_type(ev.PlacementCreated)) == 1


def test_uc05_out_of_bounds(grid_always):
    gid = _grid(grid_always)
    res = grid_always.place_image_copy(
        gid, uuid.uuid4(), CellPosition(2, 2), OccupySize(2, 2))
    assert isinstance(res.error, OutOfBounds)


def test_uc05_conflict(grid_always):
    gid = _grid(grid_always)
    _place(grid_always, gid, 0, 0, 2, 1)
    res = grid_always.place_image_copy(
        gid, uuid.uuid4(), CellPosition(1, 0), OccupySize(1, 1))
    assert isinstance(res.error, Conflict)
    assert len(res.error.conflicting_placement_ids) == 1


def test_uc05_grid_not_found(grid_always):
    res = grid_always.place_image_copy(
        uuid.uuid4(), uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res.error, NotFound)
    assert res.error.entity_kind == "GridCanvas"


def test_uc05_unknown_copy_id(grid, imgvar):
    # uses real imgvar via the grid fixture; copy does not exist -> UnknownCopyId
    gid = grid.create_grid_canvas("g", 3, 3, PixelSize(300, 300)).unwrap()
    missing = uuid.uuid4()
    res = grid.place_image_copy(gid, missing, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res.error, UnknownCopyId)
    assert res.error.copy_id == missing


# --- UC-06 -------------------------------------------------------------
def test_uc06_move_happy(grid_always, bus):
    gid = _grid(grid_always)
    pid = _place(grid_always, gid, 0, 0).unwrap()
    res = grid_always.move_placement(pid, CellPosition(2, 2))
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.PlacementMoved)) == 1


def test_uc06_not_found(grid_always):
    res = grid_always.move_placement(uuid.uuid4(), CellPosition(0, 0))
    assert isinstance(res.error, NotFound) and res.error.entity_kind == "Placement"


def test_uc06_conflict_excludes_self(grid_always):
    gid = _grid(grid_always)
    pid = _place(grid_always, gid, 0, 0).unwrap()
    # move to same position -> must NOT conflict with self
    res = grid_always.move_placement(pid, CellPosition(0, 0))
    assert isinstance(res, Ok)


# --- UC-08 -------------------------------------------------------------
def test_uc08_resize_happy(grid_always, bus):
    gid = _grid(grid_always)
    pid = _place(grid_always, gid, 0, 0).unwrap()
    res = grid_always.resize_placement_occupancy(pid, OccupySize(2, 2))
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.PlacementOccupancyResized)) == 1


def test_uc08_out_of_bounds(grid_always):
    gid = _grid(grid_always)
    pid = _place(grid_always, gid, 2, 2).unwrap()
    res = grid_always.resize_placement_occupancy(pid, OccupySize(2, 2))
    assert isinstance(res.error, OutOfBounds)


# --- UC-03 -------------------------------------------------------------
def test_uc03_weights_happy(grid_always, bus):
    gid = _grid(grid_always)
    res = grid_always.change_row_column_weights(gid, Axis.Col, (1, 2, 1))
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.RowColumnWeightsChanged)) == 1


def test_uc03_invalid_length(grid_always):
    gid = _grid(grid_always)
    res = grid_always.change_row_column_weights(gid, Axis.Col, (1, 2))
    assert isinstance(res.error, InvalidWeights)


def test_uc03_invalid_value(grid_always):
    gid = _grid(grid_always)
    res = grid_always.change_row_column_weights(gid, Axis.Col, (1, 0, 1))
    assert isinstance(res.error, InvalidWeights)


# --- UC-04 -------------------------------------------------------------
def test_uc04_toggle_happy(grid_always, bus):
    gid = _grid(grid_always)
    res = grid_always.toggle_row_column_lock(gid, Axis.Row, 1)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.RowColumnLockToggled)) == 1


def test_uc04_invalid_index(grid_always):
    gid = _grid(grid_always)
    res = grid_always.toggle_row_column_lock(gid, Axis.Row, 9)
    assert isinstance(res.error, InvalidIndex)


# --- UC-09 -------------------------------------------------------------
def test_uc09_setorder_requires_value(grid_always):
    gid = _grid(grid_always)
    pid = _place(grid_always, gid, 0, 0).unwrap()
    res = grid_always.change_placement_order(pid, OrderOperation.SetOrder, None)
    assert isinstance(res.error, InvalidOrderValue)


def test_uc09_value_forbidden_for_other_ops(grid_always):
    gid = _grid(grid_always)
    pid = _place(grid_always, gid, 0, 0).unwrap()
    res = grid_always.change_placement_order(pid, OrderOperation.BringToFront, 2)
    assert isinstance(res.error, InvalidOrderValue)


def test_uc09_bring_to_front(grid_always, bus):
    gid = _grid(grid_always)
    p1 = _place(grid_always, gid, 0, 0).unwrap()
    p2 = _place(grid_always, gid, 1, 0).unwrap()
    p3 = _place(grid_always, gid, 2, 0).unwrap()
    res = grid_always.change_placement_order(p1, OrderOperation.BringToFront)
    assert isinstance(res, Ok)
    orders = {p.id: p.placement_order for p in grid_always.list_placements(gid)}
    assert orders[p1] == 3
    assert sorted(orders.values()) == [1, 2, 3]
    assert len(bus.of_type(ev.PlacementOrderChanged)) == 1


# --- UC-10 -------------------------------------------------------------
def test_uc10_remove_not_found(grid_always):
    res = grid_always.remove_placement(uuid.uuid4())
    assert isinstance(res.error, NotFound)


# --- UC-11 -------------------------------------------------------------
def test_uc11_empty_for_unknown_grid(grid_always):
    assert grid_always.list_placements(uuid.uuid4()) == []


def test_uc11_returns_z_order(grid_always):
    gid = _grid(grid_always)
    _place(grid_always, gid, 0, 0)
    _place(grid_always, gid, 1, 0)
    placements = grid_always.list_placements(gid)
    orders = [p.placement_order for p in placements]
    assert orders == sorted(orders)


# --- UC-02 -------------------------------------------------------------
def test_uc02_happy(grid_always, bus):
    gid = _grid(grid_always)
    res = grid_always.change_grid_dimensions(gid, 4, 4)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.GridDimensionsChanged)) == 1


def test_uc02_would_orphan(grid_always):
    gid = _grid(grid_always)
    _place(grid_always, gid, 2, 2)  # occupies (2,2)
    res = grid_always.change_grid_dimensions(gid, 2, 2)  # shrink -> orphan
    assert isinstance(res.error, WouldOrphanPlacements)


def test_uc02_not_found(grid_always):
    res = grid_always.change_grid_dimensions(uuid.uuid4(), 2, 2)
    assert isinstance(res.error, NotFound)
