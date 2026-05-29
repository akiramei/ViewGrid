"""GRID_COMPOSITION tests: rules, UC happy/failure, events, anchors AT-01..AT-10."""
from __future__ import annotations

import uuid

import pytest

from grid_composition.domain import (
    CellPosition,
    GridCanvas,
    Placement,
    find_conflicts,
    fits_within_grid,
    occupied_cells,
)
from grid_composition.enums import Axis, OrderOperation
from grid_composition import events as ev
from grid_composition import failures as fail
from grid_composition.use_cases import GridCompositionUseCases
from shared.events import RecordingBus
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


class AllExist:
    def exists(self, copy_id):  # noqa: ANN001
        return True


def make_uc(bus=None, existence=None):
    return GridCompositionUseCases(
        image_copy_existence=existence or AllExist(), bus=bus or RecordingBus()
    )


def new_grid(uc, rows=3, cols=3):
    res = uc.create_grid_canvas("g", rows, cols, PixelSize(300, 300))
    assert isinstance(res, Ok)
    return res.value.id


# ---------------------------------------------------------------- Rule units
def test_r01_fits_within_grid():
    assert fits_within_grid(CellPosition(0, 0), OccupySize(3, 3), 3, 3)
    assert not fits_within_grid(CellPosition(1, 0), OccupySize(3, 1), 3, 3)  # x+w=4>3
    assert not fits_within_grid(CellPosition(-1, 0), OccupySize(1, 1), 3, 3)
    assert fits_within_grid(CellPosition(2, 2), OccupySize(1, 1), 3, 3)


def test_r02_overlap_detection():
    a = Placement(uuid.uuid4(), uuid.uuid4(), uuid.uuid4(), CellPosition(0, 0), OccupySize(2, 1), 1)
    cells = occupied_cells(CellPosition(1, 0), OccupySize(1, 1))
    assert find_conflicts(cells, [a], exclude_ids=set()) == [a.id]
    cells2 = occupied_cells(CellPosition(2, 0), OccupySize(1, 1))
    assert find_conflicts(cells2, [a], exclude_ids=set()) == []


def test_r03_grid_dimensions_positive():
    with pytest.raises(ValueError):
        GridCanvas.create("g", 0, 3, PixelSize(10, 10))
    with pytest.raises(ValueError):
        GridCanvas.create("g", 3, 0, PixelSize(10, 10))


def test_r04_weights_positive():
    uc = make_uc()
    gid = new_grid(uc)
    res = uc.change_row_column_weights(gid, Axis.Col, (1, 0, 1))
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidWeights)


def test_r05_weight_length_matches():
    uc = make_uc()
    gid = new_grid(uc, 3, 3)
    res = uc.change_row_column_weights(gid, Axis.Col, (1, 1))
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidWeights)


def test_r06_order_unique_after_place():
    uc = make_uc()
    gid = new_grid(uc)
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(1, 0), OccupySize(1, 1))
    orders = [p.placement_order for p in uc.list_placements(gid)]
    assert sorted(orders) == [1, 2]


# ---------------------------------------------------------------- UC happy paths
def test_uc01_create_grid():
    uc = make_uc()
    res = uc.create_grid_canvas("g", 2, 2, PixelSize(100, 100))
    assert isinstance(res, Ok)
    assert res.value.col_weights == (1, 1)
    assert res.value.col_locked == (False, False)


def test_uc02_change_dimensions():
    uc = make_uc()
    gid = new_grid(uc, 2, 2)
    res = uc.change_grid_dimensions(gid, 3, 3)
    assert isinstance(res, Ok)
    assert res.value.grid_cols == 3 and len(res.value.col_weights) == 3


def test_uc03_change_weights():
    uc = make_uc()
    gid = new_grid(uc, 3, 3)
    res = uc.change_row_column_weights(gid, Axis.Col, (1, 2, 1))
    assert isinstance(res, Ok) and res.value.col_weights == (1, 2, 1)


def test_uc04_toggle_lock():
    uc = make_uc()
    gid = new_grid(uc, 3, 3)
    res = uc.toggle_row_column_lock(gid, Axis.Row, 1)
    assert isinstance(res, Ok) and res.value.row_locked[1] is True


def test_uc05_place():
    uc = make_uc()
    gid = new_grid(uc)
    res = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Ok) and res.value.placement_order == 1


def test_uc06_move():
    uc = make_uc()
    gid = new_grid(uc)
    p = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    res = uc.move_placement(p.id, CellPosition(2, 2))
    assert isinstance(res, Ok) and res.value.position == CellPosition(2, 2)


def test_uc07_swap():
    uc = make_uc()
    gid = new_grid(uc)
    a = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    b = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(2, 2), OccupySize(1, 1)).value
    res = uc.swap_placements(a.id, b.id)
    assert isinstance(res, Ok)
    na, nb = res.value
    assert na.position == CellPosition(2, 2) and nb.position == CellPosition(0, 0)


def test_uc08_resize():
    uc = make_uc()
    gid = new_grid(uc)
    p = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    res = uc.resize_placement_occupancy(p.id, OccupySize(2, 2))
    assert isinstance(res, Ok) and res.value.occupy_size == OccupySize(2, 2)


def test_uc09_order_ops():
    uc = make_uc()
    gid = new_grid(uc)
    p1 = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    p2 = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(1, 0), OccupySize(1, 1)).value
    res = uc.change_placement_order(p1.id, OrderOperation.BringToFront)
    assert isinstance(res, Ok)
    final = {p.id: p.placement_order for p in uc.list_placements(gid)}
    assert final[p1.id] == 2 and final[p2.id] == 1


def test_uc10_remove():
    uc = make_uc()
    gid = new_grid(uc)
    p = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    res = uc.remove_placement(p.id)
    assert isinstance(res, Ok)
    assert uc.list_placements(gid) == []


def test_uc11_list_empty_for_missing_grid():
    uc = make_uc()
    assert uc.list_placements(uuid.uuid4()) == []


# ---------------------------------------------------------------- UC failures
def test_uc05_out_of_bounds():
    uc = make_uc()
    gid = new_grid(uc)
    res = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(2, 2), OccupySize(2, 2))
    assert isinstance(res, Err) and isinstance(res.error, fail.OutOfBounds)


def test_uc05_conflict():
    uc = make_uc()
    gid = new_grid(uc)
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    res = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Err) and isinstance(res.error, fail.Conflict)
    assert len(res.error.conflicting_placement_ids) >= 1


def test_uc05_unknown_copy():
    class NoneExist:
        def exists(self, copy_id):  # noqa: ANN001
            return False

    uc = make_uc(existence=NoneExist())
    gid = new_grid(uc)
    res = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Err) and isinstance(res.error, fail.UnknownCopyId)


def test_uc05_grid_not_found():
    uc = make_uc()
    res = uc.place_image_copy(uuid.uuid4(), uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "GridCanvas"


def test_uc09_invalid_order_value_other_op():
    uc = make_uc()
    gid = new_grid(uc)
    p = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    res = uc.change_placement_order(p.id, OrderOperation.BringToFront, order_value=2)
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidOrderValue)


def test_uc09_invalid_order_value_setorder_range():
    uc = make_uc()
    gid = new_grid(uc)
    p = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    res = uc.change_placement_order(p.id, OrderOperation.SetOrder, order_value=5)
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidOrderValue)


# ---------------------------------------------------------------- Events
def test_event_placement_created_emitted_once():
    bus = RecordingBus()
    uc = make_uc(bus=bus)
    gid = new_grid(uc)
    bus.clear()
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert len(bus.of_type(ev.PlacementCreated)) == 1


def test_event_not_emitted_on_failure():
    bus = RecordingBus()
    uc = make_uc(bus=bus)
    gid = new_grid(uc)
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    bus.clear()
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))  # conflict
    assert len(bus.of_type(ev.PlacementCreated)) == 0


# ---------------------------------------------------------------- Anchor tests
def test_at_01_first_placement_order_one():
    uc = make_uc()
    gid = new_grid(uc)
    res = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Ok) and res.value.placement_order == 1


def test_at_02_same_position_move_no_self_conflict():
    uc = make_uc()
    gid = new_grid(uc)
    a = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(2, 1)).value
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(2, 0), OccupySize(1, 1))
    res = uc.move_placement(a.id, CellPosition(0, 0))  # no-op move
    assert isinstance(res, Ok)


def test_at_03_asymmetric_swap_conflict():
    uc = make_uc()
    gid = new_grid(uc)
    a = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    b = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(1, 0), OccupySize(2, 1)).value
    res = uc.swap_placements(a.id, b.id)
    assert isinstance(res, Err) and isinstance(res.error, fail.Conflict)
    assert set(res.error.conflicting_placement_ids) == {a.id, b.id}


def test_at_04_setorder_pushes_others():
    uc = make_uc()
    gid = new_grid(uc)
    p1 = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1)).value
    p2 = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(1, 0), OccupySize(1, 1)).value
    p3 = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(2, 0), OccupySize(1, 1)).value
    uc.change_placement_order(p3.id, OrderOperation.SetOrder, order_value=1)
    final = {p.id: p.placement_order for p in uc.list_placements(gid)}
    assert final[p3.id] == 1 and final[p1.id] == 2 and final[p2.id] == 3


def test_at_05_remove_compacts_orders():
    uc = make_uc()
    gid = new_grid(uc)
    ps = [
        uc.place_image_copy(gid, uuid.uuid4(), CellPosition(i, 0), OccupySize(1, 1)).value
        for i in range(3)
    ]
    # add a 4th at row 1
    p4 = uc.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 1), OccupySize(1, 1)).value
    uc.remove_placement(ps[1].id)  # remove order=2
    final = sorted(p.placement_order for p in uc.list_placements(gid))
    assert final == [1, 2, 3]
    assert {p.placement_order for p in uc.list_placements(gid)} == {1, 2, 3}
    assert p4.id in {p.id for p in uc.list_placements(gid)}


def test_at_06_notfound_has_entity_kind():
    uc = make_uc()
    res = uc.move_placement(uuid.uuid4(), CellPosition(0, 0))
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "Placement"


def test_at_09_dimension_shrink_orphans():
    uc = make_uc()
    gid = new_grid(uc, 3, 3)
    uc.place_image_copy(gid, uuid.uuid4(), CellPosition(2, 2), OccupySize(1, 1))
    res = uc.change_grid_dimensions(gid, 2, 2)
    assert isinstance(res, Err) and isinstance(res.error, fail.WouldOrphanPlacements)


def test_at_10_toggle_lock_invalid_index():
    uc = make_uc()
    gid = new_grid(uc, 3, 3)
    res = uc.toggle_row_column_lock(gid, Axis.Col, 5)
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidIndex)
