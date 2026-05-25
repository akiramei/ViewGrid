"""Happy-path and failure-path tests for each UseCase (UC-01..UC-11)."""

from __future__ import annotations

import pytest

from grid_composition import (
    Axis,
    CellPosition,
    Conflict,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    NotFound,
    OccupySize,
    OrderOperation,
    OutOfBounds,
    PixelSize,
    UnknownCopyId,
    WouldOrphanPlacements,
    new_id,
)


# ============================================================ UC-01 =========
class TestUC01CreateGridCanvas:
    def test_happy(self, uc):
        res = uc.create_grid_canvas(
            name="my", grid_rows=2, grid_cols=4, canvas_size=PixelSize(800, 400)
        )
        assert res.is_ok
        g = res.value
        assert g.grid_rows == 2 and g.grid_cols == 4
        assert g.col_weights == (1, 1, 1, 1)
        assert g.row_weights == (1, 1)
        assert g.col_locked == (False, False, False, False)
        assert g.row_locked == (False, False)

    def test_invalid_rows(self, uc):
        res = uc.create_grid_canvas(
            name="x", grid_rows=0, grid_cols=4, canvas_size=PixelSize(100, 100)
        )
        assert res.is_err and isinstance(res.failure, InvalidDimensions)

    def test_invalid_cols(self, uc):
        res = uc.create_grid_canvas(
            name="x", grid_rows=2, grid_cols=0, canvas_size=PixelSize(100, 100)
        )
        assert res.is_err and isinstance(res.failure, InvalidDimensions)


# ============================================================ UC-02 =========
class TestUC02ChangeGridDimensions:
    def test_happy_expand(self, uc, small_grid):
        res = uc.change_grid_dimensions(
            grid_id=small_grid.id, new_grid_rows=5, new_grid_cols=5
        )
        assert res.is_ok
        g = res.value
        assert g.grid_rows == 5 and g.grid_cols == 5
        assert g.col_weights == (1, 1, 1, 1, 1)

    def test_not_found(self, uc):
        bogus = new_id()
        res = uc.change_grid_dimensions(grid_id=bogus, new_grid_rows=2, new_grid_cols=2)
        assert res.is_err and isinstance(res.failure, NotFound)
        assert res.failure.entity_kind == "GridCanvas"

    def test_would_orphan(self, uc, small_grid, a_copy_id):
        # Place at (2,2), shrink so (2,2) no longer fits
        uc.place_image_copy(
            grid_id=small_grid.id,
            copy_id=a_copy_id,
            position=CellPosition(2, 2),
            occupy_size=OccupySize(1, 1),
        )
        res = uc.change_grid_dimensions(
            grid_id=small_grid.id, new_grid_rows=2, new_grid_cols=2
        )
        assert res.is_err and isinstance(res.failure, WouldOrphanPlacements)


# ============================================================ UC-03 =========
class TestUC03ChangeWeights:
    def test_happy_col(self, uc, small_grid):
        res = uc.change_row_column_weights(
            grid_id=small_grid.id, axis=Axis.COL, weights=[1, 2, 1]
        )
        assert res.is_ok and res.value.col_weights == (1, 2, 1)

    def test_invalid_len(self, uc, small_grid):
        res = uc.change_row_column_weights(
            grid_id=small_grid.id, axis=Axis.COL, weights=[1, 1]
        )
        assert res.is_err and isinstance(res.failure, InvalidWeights)

    def test_invalid_zero(self, uc, small_grid):
        res = uc.change_row_column_weights(
            grid_id=small_grid.id, axis=Axis.COL, weights=[1, 0, 1]
        )
        assert res.is_err and isinstance(res.failure, InvalidWeights)

    def test_not_found(self, uc):
        res = uc.change_row_column_weights(grid_id=new_id(), axis=Axis.COL, weights=[1])
        assert res.is_err and isinstance(res.failure, NotFound)


# ============================================================ UC-04 =========
class TestUC04ToggleLock:
    def test_toggle_col(self, uc, small_grid):
        r1 = uc.toggle_row_column_lock(grid_id=small_grid.id, axis=Axis.COL, index=1)
        assert r1.is_ok and r1.value.col_locked == (False, True, False)
        r2 = uc.toggle_row_column_lock(grid_id=small_grid.id, axis=Axis.COL, index=1)
        assert r2.is_ok and r2.value.col_locked == (False, False, False)

    def test_invalid_index_high(self, uc, small_grid):
        r = uc.toggle_row_column_lock(grid_id=small_grid.id, axis=Axis.COL, index=3)
        assert r.is_err and isinstance(r.failure, InvalidIndex)

    def test_invalid_index_negative(self, uc, small_grid):
        r = uc.toggle_row_column_lock(grid_id=small_grid.id, axis=Axis.ROW, index=-1)
        assert r.is_err and isinstance(r.failure, InvalidIndex)


# ============================================================ UC-05 =========
class TestUC05Place:
    def test_happy(self, uc, small_grid, a_copy_id):
        res = uc.place_image_copy(
            grid_id=small_grid.id,
            copy_id=a_copy_id,
            position=CellPosition(0, 0),
            occupy_size=OccupySize(1, 1),
        )
        assert res.is_ok
        assert res.value.placement_order == 1

    def test_not_found_grid(self, uc, a_copy_id):
        res = uc.place_image_copy(
            grid_id=new_id(),
            copy_id=a_copy_id,
            position=CellPosition(0, 0),
            occupy_size=OccupySize(1, 1),
        )
        assert res.is_err and isinstance(res.failure, NotFound)

    def test_unknown_copy(self, uc, small_grid):
        res = uc.place_image_copy(
            grid_id=small_grid.id,
            copy_id=new_id(),
            position=CellPosition(0, 0),
            occupy_size=OccupySize(1, 1),
        )
        assert res.is_err and isinstance(res.failure, UnknownCopyId)

    def test_out_of_bounds(self, uc, small_grid, a_copy_id):
        res = uc.place_image_copy(
            grid_id=small_grid.id,
            copy_id=a_copy_id,
            position=CellPosition(2, 2),
            occupy_size=OccupySize(2, 2),
        )
        assert res.is_err and isinstance(res.failure, OutOfBounds)

    def test_conflict(self, uc, small_grid, a_copy_id):
        uc.place_image_copy(
            grid_id=small_grid.id,
            copy_id=a_copy_id,
            position=CellPosition(0, 0),
            occupy_size=OccupySize(2, 1),
        )
        res = uc.place_image_copy(
            grid_id=small_grid.id,
            copy_id=a_copy_id,
            position=CellPosition(1, 0),
            occupy_size=OccupySize(1, 1),
        )
        assert res.is_err and isinstance(res.failure, Conflict)


# ============================================================ UC-06 =========
class TestUC06Move:
    def test_happy(self, uc, small_grid, a_copy_id):
        p = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
        ).value
        r = uc.move_placement(placement_id=p.id, new_position=CellPosition(2, 2))
        assert r.is_ok and r.value.position == CellPosition(2, 2)

    def test_not_found(self, uc):
        r = uc.move_placement(placement_id=new_id(), new_position=CellPosition(0, 0))
        assert r.is_err and isinstance(r.failure, NotFound)

    def test_out_of_bounds(self, uc, small_grid, a_copy_id):
        p = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(2, 2),
        ).value
        r = uc.move_placement(placement_id=p.id, new_position=CellPosition(2, 2))
        assert r.is_err and isinstance(r.failure, OutOfBounds)

    def test_conflict_other(self, uc, small_grid, a_copy_id):
        p1 = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
        ).value
        p2 = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(1, 0), occupy_size=OccupySize(1, 1),
        ).value
        r = uc.move_placement(placement_id=p1.id, new_position=CellPosition(1, 0))
        assert r.is_err and isinstance(r.failure, Conflict)
        assert p2.id in r.failure.conflicting_placement_ids


# ============================================================ UC-07 =========
class TestUC07Swap:
    def test_happy(self, uc, small_grid, a_copy_id):
        a = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
        ).value
        b = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(2, 2), occupy_size=OccupySize(1, 1),
        ).value
        r = uc.swap_placements(placement_id_a=a.id, placement_id_b=b.id)
        assert r.is_ok
        na, nb = r.value
        assert na.position == CellPosition(2, 2)
        assert nb.position == CellPosition(0, 0)

    def test_not_found_either(self, uc, small_grid, a_copy_id):
        a = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
        ).value
        bogus = new_id()
        r = uc.swap_placements(placement_id_a=a.id, placement_id_b=bogus)
        assert r.is_err and isinstance(r.failure, NotFound)
        assert r.failure.entity_id == bogus

    def test_out_of_bounds(self, uc, small_grid, a_copy_id):
        # A 1x1 at (0,0), B 2x1 at (1,0). Swap -> A goes to (1,0) ok,
        # B goes to (0,0) ok (2x1 still inside). But adjust to provoke OOB:
        # Make A 1x1 at (2,2), B 2x1 at (1,0). Swap -> B at (2,2) needs (2,2),(3,2) -> OOB.
        a = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(2, 2), occupy_size=OccupySize(1, 1),
        ).value
        b = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(2, 1),
        ).value
        r = uc.swap_placements(placement_id_a=a.id, placement_id_b=b.id)
        assert r.is_err and isinstance(r.failure, OutOfBounds)


# ============================================================ UC-08 =========
class TestUC08Resize:
    def test_happy(self, uc, small_grid, a_copy_id):
        p = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
        ).value
        r = uc.resize_placement_occupancy(
            placement_id=p.id, new_occupy_size=OccupySize(2, 2)
        )
        assert r.is_ok and r.value.occupy_size == OccupySize(2, 2)

    def test_not_found(self, uc):
        r = uc.resize_placement_occupancy(
            placement_id=new_id(), new_occupy_size=OccupySize(1, 1)
        )
        assert r.is_err and isinstance(r.failure, NotFound)

    def test_out_of_bounds(self, uc, small_grid, a_copy_id):
        p = uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(2, 2), occupy_size=OccupySize(1, 1),
        ).value
        r = uc.resize_placement_occupancy(
            placement_id=p.id, new_occupy_size=OccupySize(2, 2)
        )
        assert r.is_err and isinstance(r.failure, OutOfBounds)


# ============================================================ UC-09 =========
class TestUC09Order:
    def _three(self, uc, grid, copy):
        ps = []
        for x in (0, 1, 2):
            r = uc.place_image_copy(
                grid_id=grid.id, copy_id=copy,
                position=CellPosition(x, 0), occupy_size=OccupySize(1, 1),
            )
            ps.append(r.value)
        return ps

    def test_bring_to_front(self, uc, small_grid, a_copy_id, placements):
        p1, p2, p3 = self._three(uc, small_grid, a_copy_id)
        # p1 has order 1; bring to front -> order 3
        r = uc.change_placement_order(
            placement_id=p1.id, operation=OrderOperation.BRING_TO_FRONT
        )
        assert r.is_ok
        ordered = {p.id: p.placement_order for p in placements.get_by_grid(small_grid.id)}
        assert ordered[p1.id] == 3
        assert sorted(ordered.values()) == [1, 2, 3]

    def test_set_order_pushes_others_down(self, uc, small_grid, a_copy_id, placements):
        p1, p2, p3 = self._three(uc, small_grid, a_copy_id)
        # set p3.order = 1
        r = uc.change_placement_order(
            placement_id=p3.id, operation=OrderOperation.SET_ORDER, order_value=1
        )
        assert r.is_ok
        ordered = {p.id: p.placement_order for p in placements.get_by_grid(small_grid.id)}
        assert ordered[p3.id] == 1
        assert ordered[p1.id] == 2
        assert ordered[p2.id] == 3

    def test_set_order_missing_value(self, uc, small_grid, a_copy_id):
        ps = self._three(uc, small_grid, a_copy_id)
        r = uc.change_placement_order(
            placement_id=ps[0].id, operation=OrderOperation.SET_ORDER, order_value=None
        )
        assert r.is_err and isinstance(r.failure, InvalidOrderValue)

    def test_set_order_out_of_range(self, uc, small_grid, a_copy_id):
        ps = self._three(uc, small_grid, a_copy_id)
        r = uc.change_placement_order(
            placement_id=ps[0].id, operation=OrderOperation.SET_ORDER, order_value=99
        )
        assert r.is_err and isinstance(r.failure, InvalidOrderValue)

    def test_other_op_rejects_value(self, uc, small_grid, a_copy_id):
        ps = self._three(uc, small_grid, a_copy_id)
        r = uc.change_placement_order(
            placement_id=ps[0].id,
            operation=OrderOperation.BRING_TO_FRONT,
            order_value=2,
        )
        assert r.is_err and isinstance(r.failure, InvalidOrderValue)

    def test_not_found(self, uc):
        r = uc.change_placement_order(
            placement_id=new_id(), operation=OrderOperation.BRING_TO_FRONT
        )
        assert r.is_err and isinstance(r.failure, NotFound)


# ============================================================ UC-10 =========
class TestUC10Remove:
    def test_happy(self, uc, small_grid, a_copy_id, placements):
        ps = []
        for x in (0, 1, 2):
            r = uc.place_image_copy(
                grid_id=small_grid.id, copy_id=a_copy_id,
                position=CellPosition(x, 0), occupy_size=OccupySize(1, 1),
            )
            ps.append(r.value)
        # Remove middle
        r = uc.remove_placement(placement_id=ps[1].id)
        assert r.is_ok
        # Remaining orders should be compact 1..2
        remaining = placements.get_by_grid(small_grid.id)
        orders = sorted(p.placement_order for p in remaining)
        assert orders == [1, 2]

    def test_not_found(self, uc):
        r = uc.remove_placement(placement_id=new_id())
        assert r.is_err and isinstance(r.failure, NotFound)


# ============================================================ UC-11 =========
class TestUC11List:
    def test_happy_sorted(self, uc, small_grid, a_copy_id):
        for x in (0, 1, 2):
            uc.place_image_copy(
                grid_id=small_grid.id, copy_id=a_copy_id,
                position=CellPosition(x, 0), occupy_size=OccupySize(1, 1),
            )
        r = uc.list_placements(grid_id=small_grid.id)
        assert r.is_ok
        # ascending z-order
        assert [p.placement_order for p in r.value] == [1, 2, 3]

    def test_missing_grid_returns_empty(self, uc):
        r = uc.list_placements(grid_id=new_id())
        assert r.is_ok and r.value == []
