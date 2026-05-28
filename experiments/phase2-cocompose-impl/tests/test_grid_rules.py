"""GRID Rule unit tests (R-01..R-09)."""

import uuid

import pytest

from grid_composition.domain import (
    CellPosition,
    GridCanvas,
    Placement,
    fits_within_grid,
    find_overlaps,
    occupied_cells,
)
from shared.value_objects import OccupySize, PixelSize


def _placement(pos, size, order=1):
    return Placement(
        id=uuid.uuid4(), grid_id=uuid.uuid4(), copy_id=uuid.uuid4(),
        position=CellPosition(*pos), occupy_size=OccupySize(*size),
        placement_order=order,
        created_at=__import__("datetime").datetime.now(
            __import__("datetime").timezone.utc),
    )


# --- R-01 ---------------------------------------------------------------
def test_r01_exact_fit():
    assert fits_within_grid(CellPosition(0, 0), OccupySize(3, 3), 3, 3)


def test_r01_one_cell_overflow():
    assert not fits_within_grid(CellPosition(1, 0), OccupySize(3, 1), 3, 3)


def test_r01_negative_position():
    assert not fits_within_grid(CellPosition(-1, 0), OccupySize(1, 1), 3, 3)


# --- R-02 ---------------------------------------------------------------
def test_r02_no_overlap():
    others = [_placement((0, 0), (1, 1))]
    assert find_overlaps(CellPosition(1, 0), OccupySize(1, 1), others) == []


def test_r02_overlap_detected():
    p = _placement((0, 0), (2, 1))
    overlaps = find_overlaps(CellPosition(1, 0), OccupySize(1, 1), [p])
    assert overlaps == [p.id]


def test_r02_occupied_cells():
    cells = occupied_cells(CellPosition(1, 1), OccupySize(2, 2))
    assert cells == {(1, 1), (2, 1), (1, 2), (2, 2)}


# --- R-03 ---------------------------------------------------------------
def test_r03_rejects_zero_dimensions():
    with pytest.raises(ValueError):
        GridCanvas.create("g", 0, 3, PixelSize(10, 10))
    with pytest.raises(ValueError):
        GridCanvas.create("g", 3, 0, PixelSize(10, 10))


# --- R-04 / R-05 (construction) ----------------------------------------
def test_r04_weights_must_be_positive():
    import datetime as dt
    now = dt.datetime.now(dt.timezone.utc)
    with pytest.raises(ValueError):
        GridCanvas(id=uuid.uuid4(), name="g", grid_rows=1, grid_cols=2,
                   col_weights=(1, 0), row_weights=(1,), col_locked=(False, False),
                   row_locked=(False,), canvas_size=PixelSize(10, 10),
                   created_at=now, updated_at=now)


def test_r05_weight_length_must_match():
    import datetime as dt
    now = dt.datetime.now(dt.timezone.utc)
    with pytest.raises(ValueError):
        GridCanvas(id=uuid.uuid4(), name="g", grid_rows=1, grid_cols=2,
                   col_weights=(1,), row_weights=(1,), col_locked=(False, False),
                   row_locked=(False,), canvas_size=PixelSize(10, 10),
                   created_at=now, updated_at=now)
