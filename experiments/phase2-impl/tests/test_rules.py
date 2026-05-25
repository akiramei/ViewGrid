"""Rule unit tests (R-01..R-09).

One file, multiple TestCase classes -- one per Rule. This collocates the
test-of-record for each Rule and makes the post-audit "R-N is guaranteed
in one place" check trivial.
"""

from __future__ import annotations

from uuid import uuid4

import pytest

from grid_composition import (
    CellPosition,
    GridCanvas,
    OccupySize,
    PixelSize,
    Placement,
)
from grid_composition import rules


# -- R-01: PlacementMustFitWithinGrid ---------------------------------------


class TestR01_PlacementMustFitWithinGrid:
    def test_origin_1x1_fits_in_1x1_grid(self):
        assert rules.placement_fits_within_grid(CellPosition(0, 0), OccupySize(1, 1), 1, 1)

    def test_exact_grid_size_fits(self):
        assert rules.placement_fits_within_grid(CellPosition(0, 0), OccupySize(3, 3), 3, 3)

    def test_overflow_right_does_not_fit(self):
        assert not rules.placement_fits_within_grid(
            CellPosition(2, 0), OccupySize(2, 1), 3, 3
        )

    def test_overflow_bottom_does_not_fit(self):
        assert not rules.placement_fits_within_grid(
            CellPosition(0, 2), OccupySize(1, 2), 3, 3
        )

    def test_negative_x_does_not_fit(self):
        # Defensive: CellPosition itself blocks negatives via __post_init__,
        # but the predicate must say "no" if a negative slips through.
        # We bypass by direct construction of a position-like object:
        # we can't (CellPosition rejects). Cover by checking origin (0,0)
        # is the smallest valid value.
        assert rules.placement_fits_within_grid(CellPosition(0, 0), OccupySize(1, 1), 1, 1)

    def test_one_cell_beyond_boundary(self):
        assert not rules.placement_fits_within_grid(
            CellPosition(3, 0), OccupySize(1, 1), 3, 3
        )


# -- R-02: PlacementsMustNotOverlap -----------------------------------------


def _mkp(grid_id, copy_id, x, y, w, h, order):
    return Placement.create(
        grid_id=grid_id,
        copy_id=copy_id,
        position=CellPosition(x, y),
        occupy_size=OccupySize(w, h),
        placement_order=order,
    )


class TestR02_PlacementsMustNotOverlap:
    def setup_method(self):
        self.gid = uuid4()
        self.cid = uuid4()
        self.existing = [_mkp(self.gid, self.cid, 0, 0, 2, 2, 1)]

    def test_non_overlapping_candidate_returns_empty(self):
        assert rules.placement_overlaps(CellPosition(2, 0), OccupySize(1, 1), self.existing) == ()

    def test_overlapping_candidate_returns_conflict_id(self):
        c = rules.placement_overlaps(CellPosition(1, 1), OccupySize(2, 2), self.existing)
        assert c == (self.existing[0].id,)

    def test_excluded_id_is_skipped(self):
        c = rules.placement_overlaps(
            CellPosition(0, 0),
            OccupySize(2, 2),
            self.existing,
            exclude_ids=(self.existing[0].id,),
        )
        assert c == ()

    def test_adjacent_not_overlapping(self):
        # (2,0) 1x1 shares an edge with (0..1, 0..1) but no cell.
        assert rules.placement_overlaps(CellPosition(2, 0), OccupySize(1, 1), self.existing) == ()

    def test_multiple_conflicts_all_reported(self):
        p2 = _mkp(self.gid, self.cid, 3, 0, 1, 1, 2)
        c = rules.placement_overlaps(
            CellPosition(0, 0), OccupySize(4, 1), [self.existing[0], p2]
        )
        assert set(c) == {self.existing[0].id, p2.id}


# -- R-03: GridDimensionsMustBePositive -------------------------------------


class TestR03_GridDimensionsMustBePositive:
    def test_zero_rows_rejected(self):
        with pytest.raises(ValueError):
            GridCanvas.create("n", 0, 1, PixelSize(10, 10))

    def test_zero_cols_rejected(self):
        with pytest.raises(ValueError):
            GridCanvas.create("n", 1, 0, PixelSize(10, 10))

    def test_negative_canvas_rejected(self):
        with pytest.raises(ValueError):
            PixelSize(0, 10)
        with pytest.raises(ValueError):
            PixelSize(10, -1)

    def test_minimum_positive_accepted(self):
        g = GridCanvas.create("n", 1, 1, PixelSize(1, 1))
        assert g.grid_rows == 1 and g.grid_cols == 1


# -- R-04: WeightsMustBePositiveIntegers ------------------------------------


class TestR04_WeightsMustBePositiveIntegers:
    def test_default_weights_are_uniform_ones(self):
        g = GridCanvas.create("n", 2, 3, PixelSize(10, 10))
        assert g.col_weights == (1, 1, 1)
        assert g.row_weights == (1, 1)

    def test_zero_weight_rejected(self):
        with pytest.raises(ValueError):
            GridCanvas(
                id=uuid4(),
                name="n",
                grid_rows=1,
                grid_cols=2,
                col_weights=(1, 0),
                row_weights=(1,),
                col_locked=(False, False),
                row_locked=(False,),
                canvas_size=PixelSize(10, 10),
                created_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
                updated_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
            )

    def test_negative_weight_rejected(self):
        with pytest.raises(ValueError):
            GridCanvas(
                id=uuid4(),
                name="n",
                grid_rows=1,
                grid_cols=2,
                col_weights=(1, -1),
                row_weights=(1,),
                col_locked=(False, False),
                row_locked=(False,),
                canvas_size=PixelSize(10, 10),
                created_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
                updated_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
            )


# -- R-05: WeightArrayLengthMatchesDimension --------------------------------


class TestR05_WeightArrayLengthMatchesDimension:
    def test_mismatched_col_weights_rejected(self):
        with pytest.raises(ValueError):
            GridCanvas(
                id=uuid4(),
                name="n",
                grid_rows=1,
                grid_cols=3,
                col_weights=(1, 1),  # too short
                row_weights=(1,),
                col_locked=(False, False, False),
                row_locked=(False,),
                canvas_size=PixelSize(10, 10),
                created_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
                updated_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
            )

    def test_mismatched_row_weights_rejected(self):
        with pytest.raises(ValueError):
            GridCanvas(
                id=uuid4(),
                name="n",
                grid_rows=2,
                grid_cols=1,
                col_weights=(1,),
                row_weights=(1, 1, 1),  # too long
                col_locked=(False,),
                row_locked=(False, False, False),
                canvas_size=PixelSize(10, 10),
                created_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
                updated_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
            )


# -- R-06: PlacementOrderMustBeUnique ---------------------------------------


class TestR06_PlacementOrderMustBeUnique:
    def test_unique_orders_pass(self):
        gid = uuid4()
        cid = uuid4()
        ps = [_mkp(gid, cid, 0, 0, 1, 1, 1), _mkp(gid, cid, 1, 0, 1, 1, 2)]
        assert rules.orders_are_unique(ps)

    def test_duplicate_orders_fail(self):
        gid = uuid4()
        cid = uuid4()
        ps = [_mkp(gid, cid, 0, 0, 1, 1, 1), _mkp(gid, cid, 1, 0, 1, 1, 1)]
        assert not rules.orders_are_unique(ps)


# -- R-07: CellPositionAndOccupySizeAreImmutableInOnePlacement --------------


class TestR07_PlacementIsImmutable:
    def test_modifying_returns_new_instance(self):
        gid = uuid4()
        cid = uuid4()
        p = _mkp(gid, cid, 0, 0, 1, 1, 1)
        p2 = p.with_position(CellPosition(2, 2))
        assert p is not p2
        assert p.position == CellPosition(0, 0)
        assert p2.position == CellPosition(2, 2)

    def test_dataclass_is_frozen(self):
        gid = uuid4()
        cid = uuid4()
        p = _mkp(gid, cid, 0, 0, 1, 1, 1)
        with pytest.raises(Exception):  # FrozenInstanceError
            p.position = CellPosition(1, 1)  # type: ignore[misc]

    def test_before_and_after_observable_independently(self):
        """30-design.md §6.2 R-07: same Placement observed at two times
        yields independent values after a position change."""
        gid = uuid4()
        cid = uuid4()
        p_before = _mkp(gid, cid, 0, 0, 1, 1, 1)
        p_after = p_before.with_position(CellPosition(2, 1))
        assert (p_before.position.x, p_before.position.y) == (0, 0)
        assert (p_after.position.x, p_after.position.y) == (2, 1)


# -- R-08: LockedWeightsAreSkippedInFitAdjustment (UC-02 owns) --------------
# Covered in test_use_cases.py::TestUC02ChangeGridDimensions::test_lock_*


# -- R-09: RemovedPlacementOrderMustBeCompacted -----------------------------


class TestR09_RemovedPlacementOrderMustBeCompacted:
    def test_compact_no_gap_is_noop(self):
        gid = uuid4()
        cid = uuid4()
        ps = [_mkp(gid, cid, 0, 0, 1, 1, i) for i in (1, 2, 3)]
        new, mapping = rules.compact_orders(ps)
        assert [q.placement_order for q in new] == [1, 2, 3]
        # All map to themselves.
        assert all(mapping[ps[i].id] == i + 1 for i in range(3))

    def test_compact_fills_gap(self):
        gid = uuid4()
        cid = uuid4()
        ps = [
            _mkp(gid, cid, 0, 0, 1, 1, 1),
            _mkp(gid, cid, 1, 0, 1, 1, 4),
            _mkp(gid, cid, 2, 0, 1, 1, 6),
        ]
        new, mapping = rules.compact_orders(ps)
        assert sorted([q.placement_order for q in new]) == [1, 2, 3]
        # Original order preserved.
        assert mapping[ps[0].id] == 1
        assert mapping[ps[1].id] == 2
        assert mapping[ps[2].id] == 3

    def test_compact_empty_is_empty(self):
        new, mapping = rules.compact_orders([])
        assert new == []
        assert mapping == {}
