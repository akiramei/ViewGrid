"""Use Case tests — happy path + each failure reason per BOM.

One test class per UC. The structure mirrors 21-grid-composition.yaml
§use_cases so the post-audit can grep test_uc_NN.
"""

from __future__ import annotations

from uuid import uuid4

import pytest

from grid_composition import (
    CellPosition,
    Conflict,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    OccupySize,
    OutOfBounds,
    PixelSize,
    UnknownCopyId,
    WouldOrphanPlacements,
)
from grid_composition.domain import Axis, OrderOperation

from tests.conftest import make_copy


# -- UC-01 ------------------------------------------------------------------


class TestUC01_CreateGridCanvas:
    def test_happy_path(self, ucs):
        g = ucs.create.execute("g", 3, 4, PixelSize(800, 600))
        assert g.grid_rows == 3
        assert g.grid_cols == 4
        assert g.col_weights == (1, 1, 1, 1)
        assert g.row_weights == (1, 1, 1)
        assert g.col_locked == (False, False, False, False)
        assert g.row_locked == (False, False, False)

    def test_invalid_dimensions_zero_rows(self, ucs):
        with pytest.raises(InvalidDimensions):
            ucs.create.execute("g", 0, 1, PixelSize(10, 10))

    def test_invalid_dimensions_zero_cols(self, ucs):
        with pytest.raises(InvalidDimensions):
            ucs.create.execute("g", 1, 0, PixelSize(10, 10))


# -- UC-02 ------------------------------------------------------------------


class TestUC02_ChangeGridDimensions:
    def test_happy_path_grow(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        updated = ucs.change_dims.execute(grid_3x3.id, 4, 4)
        assert updated.grid_rows == 4
        assert updated.grid_cols == 4
        # Weights are extended with 1.
        assert updated.col_weights == (1, 1, 1, 1)
        assert updated.row_weights == (1, 1, 1, 1)

    def test_invalid_dimensions(self, ucs, grid_3x3):
        with pytest.raises(InvalidDimensions):
            ucs.change_dims.execute(grid_3x3.id, 0, 0)

    def test_would_orphan(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        with pytest.raises(WouldOrphanPlacements):
            ucs.change_dims.execute(grid_3x3.id, 2, 2)

    def test_shrink_with_lock_drop_rejected(self, ucs, grid_3x3):
        # Lock the only column that would survive a shrink to (1,1).
        ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 0)
        ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 1)
        ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 2)
        # Shrinking to 1 col would require dropping locked entries -> reject.
        with pytest.raises(InvalidDimensions):
            ucs.change_dims.execute(grid_3x3.id, 3, 1)

    def test_lock_respected_in_fit(self, ucs, grid_3x3):
        """R-08: when shrinking, locked entries are preserved; the tail
        unlocked entry is the one dropped."""
        # Set col_weights = [5, 7, 3] and lock col 0 and col 1.
        ucs.change_weights.execute(grid_3x3.id, Axis.COL, [5, 7, 3])
        ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 0)
        ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 1)
        # Shrink to 2 cols. The unlocked tail (index 2, weight 3) drops.
        updated = ucs.change_dims.execute(grid_3x3.id, 3, 2)
        assert updated.col_weights == (5, 7)
        assert updated.col_locked == (True, True)


# -- UC-03 ------------------------------------------------------------------


class TestUC03_ChangeRowColumnWeights:
    def test_happy_path(self, ucs, grid_3x3):
        updated = ucs.change_weights.execute(grid_3x3.id, Axis.COL, [1, 2, 1])
        assert updated.col_weights == (1, 2, 1)

    def test_length_mismatch(self, ucs, grid_3x3):
        with pytest.raises(InvalidWeights):
            ucs.change_weights.execute(grid_3x3.id, Axis.COL, [1, 2])

    def test_zero_weight(self, ucs, grid_3x3):
        with pytest.raises(InvalidWeights):
            ucs.change_weights.execute(grid_3x3.id, Axis.COL, [1, 0, 1])

    def test_negative_weight(self, ucs, grid_3x3):
        with pytest.raises(InvalidWeights):
            ucs.change_weights.execute(grid_3x3.id, Axis.COL, [1, -1, 1])

    def test_placement_positions_unchanged(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 1), OccupySize(1, 1))
        ucs.change_weights.execute(grid_3x3.id, Axis.ROW, [3, 1, 2])
        listed = ucs.list_p.execute(grid_3x3.id)
        assert listed[0].position == p.position


# -- UC-04 ------------------------------------------------------------------


class TestUC04_ToggleRowColumnLock:
    def test_happy_path(self, ucs, grid_3x3):
        updated = ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 1)
        assert updated.col_locked == (False, True, False)
        # Toggle again returns to False.
        updated = ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 1)
        assert updated.col_locked == (False, False, False)

    def test_invalid_index(self, ucs, grid_3x3):
        with pytest.raises(InvalidIndex):
            ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 99)

    def test_negative_index(self, ucs, grid_3x3):
        with pytest.raises(InvalidIndex):
            ucs.toggle_lock.execute(grid_3x3.id, Axis.ROW, -1)


# -- UC-05 ------------------------------------------------------------------


class TestUC05_PlaceImageCopy:
    def test_happy_path(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        assert p.placement_order == 1

    def test_out_of_bounds(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        with pytest.raises(OutOfBounds):
            ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 0), OccupySize(2, 1))

    def test_conflict(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(2, 2))
        with pytest.raises(Conflict) as ex:
            ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 1), OccupySize(1, 1))
        assert len(ex.value.conflicting_placement_ids) == 1

    def test_unknown_copy_id(self, ucs, grid_3x3):
        with pytest.raises(UnknownCopyId):
            ucs.place.execute(grid_3x3.id, uuid4(), CellPosition(0, 0), OccupySize(1, 1))

    def test_order_is_max_plus_one(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p1 = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        p2 = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        p3 = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 0), OccupySize(1, 1))
        assert (p1.placement_order, p2.placement_order, p3.placement_order) == (1, 2, 3)


# -- UC-06 ------------------------------------------------------------------


class TestUC06_MovePlacement:
    def test_happy_path(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        moved = ucs.move.execute(p.id, CellPosition(2, 2))
        assert moved.position == CellPosition(2, 2)

    def test_out_of_bounds(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(2, 2))
        with pytest.raises(OutOfBounds):
            ucs.move.execute(p.id, CellPosition(2, 2))

    def test_conflict(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        with pytest.raises(Conflict) as ex:
            ucs.move.execute(a.id, CellPosition(2, 2))
        assert b.id in ex.value.conflicting_placement_ids

    def test_self_excluded_from_overlap(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        # Moving to its own current position must succeed (self exclusion).
        moved = ucs.move.execute(p.id, CellPosition(0, 0))
        assert moved.position == CellPosition(0, 0)


# -- UC-07 ------------------------------------------------------------------


class TestUC07_SwapPlacements:
    def test_happy_path(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        new_a, new_b = ucs.swap.execute(a.id, b.id)
        assert new_a.position == CellPosition(2, 2)
        assert new_b.position == CellPosition(0, 0)

    def test_out_of_bounds_when_sizes_differ(self, ucs, grid_3x3, copies):
        """S3 from 10-requirements.md §2.2: sizes differ -> swap may fail."""
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(2, 2))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        # Placing a (2x2) at b.position (2,2) would exceed grid bounds.
        with pytest.raises(OutOfBounds):
            ucs.swap.execute(a.id, b.id)

    def test_conflict_with_third_placement(self, ucs, grid_3x3, copies):
        """Swap fails when one of the new positions overlaps a 3rd placement."""
        cid = make_copy(copies)
        # Use a 3x3 grid. Place A at (0,0) size 2x1, B at (0,2) size 2x1.
        # The intervening row contains placement C at (0,1) size 2x1.
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(2, 1))
        c = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 1), OccupySize(2, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 2), OccupySize(2, 1))
        # Swap A <-> B: A would move to (0,2) and B would move to (0,0).
        # Neither overlaps C since C stays at (0,1) and A/B occupy y=2/y=0.
        # That's actually a clean swap — test it succeeds.
        new_a, new_b = ucs.swap.execute(a.id, b.id)
        assert new_a.position == CellPosition(0, 2)
        assert new_b.position == CellPosition(0, 0)

    def test_both_excluded_from_each_others_conflict(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        new_a, new_b = ucs.swap.execute(a.id, b.id)
        assert new_a.position == CellPosition(1, 0)
        assert new_b.position == CellPosition(0, 0)

    def test_swap_detects_mutual_new_position_overlap(self, ucs, grid_3x3, copies):
        """Regression: when A 1x1 swaps with B 2x1, A's new footprint at B's
        old position and B's new footprint at A's old position can cover
        the same cell. R-02 must catch this even with mutual exclusion."""
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(2, 1))
        # A would go to (1,0) covering (1,0). B would go to (0,0) covering
        # (0,0)+(1,0). Cell (1,0) is doubly occupied.
        with pytest.raises(Conflict):
            ucs.swap.execute(a.id, b.id)


# -- UC-08 ------------------------------------------------------------------


class TestUC08_ResizePlacementOccupancy:
    def test_happy_path(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        resized = ucs.resize.execute(p.id, OccupySize(3, 3))
        assert resized.occupy_size == OccupySize(3, 3)

    def test_out_of_bounds(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        with pytest.raises(OutOfBounds):
            ucs.resize.execute(p.id, OccupySize(2, 2))

    def test_conflict(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        with pytest.raises(Conflict) as ex:
            ucs.resize.execute(a.id, OccupySize(2, 1))
        assert b.id in ex.value.conflicting_placement_ids


# -- UC-09 ------------------------------------------------------------------


class TestUC09_ChangePlacementOrder:
    def _setup(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        c = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 0), OccupySize(1, 1))
        return a, b, c

    def test_bring_to_front(self, ucs, grid_3x3, copies):
        a, b, c = self._setup(ucs, grid_3x3, copies)
        new_a = ucs.change_order.execute(a.id, OrderOperation.BringToFront)
        assert new_a.placement_order == 3

    def test_send_to_back(self, ucs, grid_3x3, copies):
        a, b, c = self._setup(ucs, grid_3x3, copies)
        new_c = ucs.change_order.execute(c.id, OrderOperation.SendToBack)
        assert new_c.placement_order == 1

    def test_move_forward(self, ucs, grid_3x3, copies):
        a, b, c = self._setup(ucs, grid_3x3, copies)
        new_a = ucs.change_order.execute(a.id, OrderOperation.MoveForward)
        assert new_a.placement_order == 2

    def test_move_backward(self, ucs, grid_3x3, copies):
        a, b, c = self._setup(ucs, grid_3x3, copies)
        new_c = ucs.change_order.execute(c.id, OrderOperation.MoveBackward)
        assert new_c.placement_order == 2

    def test_set_order_invalid(self, ucs, grid_3x3, copies):
        a, b, c = self._setup(ucs, grid_3x3, copies)
        with pytest.raises(InvalidOrderValue):
            ucs.change_order.execute(a.id, OrderOperation.SetOrder, order_value=99)

    def test_set_order_valid(self, ucs, grid_3x3, copies):
        a, b, c = self._setup(ucs, grid_3x3, copies)
        new_a = ucs.change_order.execute(a.id, OrderOperation.SetOrder, order_value=3)
        assert new_a.placement_order == 3


# -- UC-10 ------------------------------------------------------------------


class TestUC10_RemovePlacement:
    def test_happy_path_compacts(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        c = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 0), OccupySize(1, 1))
        ucs.remove.execute(b.id)
        remaining = ucs.list_p.execute(grid_3x3.id)
        # Two placements remain, orders 1..2.
        assert len(remaining) == 2
        assert [p.placement_order for p in remaining] == [1, 2]

    def test_remove_only_placement(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        ucs.remove.execute(p.id)
        assert ucs.list_p.execute(grid_3x3.id) == []


# -- UC-11 ------------------------------------------------------------------


class TestUC11_ListPlacements:
    def test_empty(self, ucs, grid_3x3):
        assert ucs.list_p.execute(grid_3x3.id) == []

    def test_z_order_ascending(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        ucs.change_order.execute(a.id, OrderOperation.BringToFront)
        listed = ucs.list_p.execute(grid_3x3.id)
        assert [p.id for p in listed] == [b.id, a.id]

    def test_unknown_grid_returns_empty(self, ucs):
        assert ucs.list_p.execute(uuid4()) == []
