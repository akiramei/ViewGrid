"""Unit tests for the Rule predicates and entity-level invariants.

Covers R-01, R-02, R-03, R-04, R-05, R-06, R-07. R-08 and R-09 are
exercised through their owning UseCases (UC-02 and UC-10), see
``test_use_cases.py`` and ``test_anchors.py``.
"""

from __future__ import annotations

import pytest

from grid_composition import (
    CellPosition,
    GridCanvas,
    OccupySize,
    PixelSize,
    Placement,
    new_id,
)
from grid_composition.rules import fits_within_grid, find_conflicts, occupied_cells


# ----------------------------- R-01 ----------------------------------------

def _grid(rows: int, cols: int) -> GridCanvas:
    return GridCanvas.create(
        id=new_id(),
        name="g",
        grid_rows=rows,
        grid_cols=cols,
        canvas_size=PixelSize(width=100, height=100),
    )


def test_r01_exact_fit():
    g = _grid(3, 3)
    assert fits_within_grid(g, CellPosition(0, 0), OccupySize(3, 3))


def test_r01_one_off_right():
    g = _grid(3, 3)
    assert not fits_within_grid(g, CellPosition(1, 0), OccupySize(3, 1))


def test_r01_one_off_bottom():
    g = _grid(3, 3)
    assert not fits_within_grid(g, CellPosition(0, 1), OccupySize(1, 3))


def test_r01_at_corner():
    g = _grid(3, 3)
    assert fits_within_grid(g, CellPosition(2, 2), OccupySize(1, 1))


# ----------------------------- R-02 ----------------------------------------

def test_r02_no_overlap_returns_empty():
    g = _grid(3, 3)
    existing = [
        Placement(
            id=new_id(),
            grid_id=g.id,
            copy_id=new_id(),
            position=CellPosition(0, 0),
            occupy_size=OccupySize(1, 1),
            placement_order=1,
        ),
    ]
    cand = occupied_cells(CellPosition(1, 0), OccupySize(1, 1))
    assert find_conflicts(cand, existing) == ()


def test_r02_overlap_returns_ids():
    g = _grid(3, 3)
    p_id = new_id()
    existing = [
        Placement(
            id=p_id,
            grid_id=g.id,
            copy_id=new_id(),
            position=CellPosition(0, 0),
            occupy_size=OccupySize(2, 1),
            placement_order=1,
        ),
    ]
    cand = occupied_cells(CellPosition(1, 0), OccupySize(1, 1))
    assert find_conflicts(cand, existing) == (p_id,)


def test_r02_exclude_self():
    p_id = new_id()
    g = _grid(3, 3)
    existing = [
        Placement(
            id=p_id,
            grid_id=g.id,
            copy_id=new_id(),
            position=CellPosition(0, 0),
            occupy_size=OccupySize(1, 1),
            placement_order=1,
        )
    ]
    cand = occupied_cells(CellPosition(0, 0), OccupySize(1, 1))
    assert find_conflicts(cand, existing, exclude_ids=[p_id]) == ()


# ----------------------------- R-03 ----------------------------------------

def test_r03_zero_rows_rejected():
    with pytest.raises(ValueError):
        _grid(0, 3)


def test_r03_zero_cols_rejected():
    with pytest.raises(ValueError):
        _grid(3, 0)


def test_r03_pixelsize_must_be_positive():
    with pytest.raises(ValueError):
        PixelSize(width=0, height=10)


# ----------------------------- R-04 ----------------------------------------

def test_r04_non_positive_weight_rejected():
    with pytest.raises(ValueError):
        GridCanvas(
            id=new_id(),
            name="g",
            grid_rows=2,
            grid_cols=2,
            col_weights=(1, 0),  # invalid
            row_weights=(1, 1),
            col_locked=(False, False),
            row_locked=(False, False),
            canvas_size=PixelSize(10, 10),
            created_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
            updated_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
        )


# ----------------------------- R-05 ----------------------------------------

def test_r05_col_weights_length_mismatch():
    with pytest.raises(ValueError):
        GridCanvas(
            id=new_id(),
            name="g",
            grid_rows=2,
            grid_cols=2,
            col_weights=(1,),  # too short
            row_weights=(1, 1),
            col_locked=(False, False),
            row_locked=(False, False),
            canvas_size=PixelSize(10, 10),
            created_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
            updated_at=__import__("datetime").datetime.now(__import__("datetime").timezone.utc),
        )


# ----------------------------- R-06 ----------------------------------------
# Constructive: uniqueness is enforced by UC-05/UC-09/UC-10 building 1..N.
# We verify the contract by exercising those UseCases below — see test_use_cases.


# ----------------------------- R-07 ----------------------------------------

def test_r07_placement_is_frozen():
    p = Placement(
        id=new_id(),
        grid_id=new_id(),
        copy_id=new_id(),
        position=CellPosition(0, 0),
        occupy_size=OccupySize(1, 1),
        placement_order=1,
    )
    with pytest.raises(Exception):
        # dataclass(frozen=True) raises FrozenInstanceError (subclass of AttributeError)
        p.position = CellPosition(1, 1)  # type: ignore[misc]


def test_r07_before_after_observable():
    """A change is expressed by producing a new Placement; the original is
    untouched, so both snapshots are independently observable."""
    from dataclasses import replace
    p = Placement(
        id=new_id(),
        grid_id=new_id(),
        copy_id=new_id(),
        position=CellPosition(0, 0),
        occupy_size=OccupySize(1, 1),
        placement_order=1,
    )
    p2 = replace(p, position=CellPosition(2, 2))
    assert p.position == CellPosition(0, 0)
    assert p2.position == CellPosition(2, 2)
    assert p.id == p2.id  # same logical placement
