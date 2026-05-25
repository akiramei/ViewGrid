"""Shared pytest fixtures."""

from __future__ import annotations

import pytest

from grid_composition import (
    GridCompositionUseCases,
    InMemoryGridCanvasRepository,
    InMemoryImageCopyExistenceCheck,
    InMemoryPlacementRepository,
    PixelSize,
    RecordingBus,
    new_id,
)


@pytest.fixture
def bus() -> RecordingBus:
    return RecordingBus()


@pytest.fixture
def grids():
    return InMemoryGridCanvasRepository()


@pytest.fixture
def placements():
    return InMemoryPlacementRepository()


@pytest.fixture
def copies():
    return InMemoryImageCopyExistenceCheck()


@pytest.fixture
def uc(grids, placements, copies, bus):
    return GridCompositionUseCases(
        grid_repo=grids,
        placement_repo=placements,
        image_copy_check=copies,
        event_bus=bus,
    )


@pytest.fixture
def a_copy_id(copies):
    cid = new_id()
    copies.add(cid)
    return cid


@pytest.fixture
def small_grid(uc, bus):
    """3x3 grid, fresh bus state cleared after creation."""
    res = uc.create_grid_canvas(
        name="g",
        grid_rows=3,
        grid_cols=3,
        canvas_size=PixelSize(width=300, height=300),
    )
    assert res.is_ok
    bus.clear()
    return res.value
