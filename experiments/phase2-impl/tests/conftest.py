"""Shared pytest fixtures."""

from __future__ import annotations

import sys
from pathlib import Path

# Make the src/ package importable without an editable install.
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "src"))
sys.path.insert(0, str(ROOT))

from uuid import UUID, uuid4  # noqa: E402

import pytest  # noqa: E402

from grid_composition import (  # noqa: E402
    ChangeGridDimensions,
    ChangePlacementOrder,
    ChangeRowColumnWeights,
    CreateGridCanvas,
    InMemoryGridCanvasRepository,
    InMemoryImageCopyExistenceCheck,
    InMemoryPlacementRepository,
    ListPlacements,
    MovePlacement,
    PixelSize,
    PlaceImageCopy,
    RemovePlacement,
    ResizePlacementOccupancy,
    SwapPlacements,
    ToggleRowColumnLock,
)
from grid_composition.events import RecordingBus  # noqa: E402


@pytest.fixture
def grids() -> InMemoryGridCanvasRepository:
    return InMemoryGridCanvasRepository()


@pytest.fixture
def placements() -> InMemoryPlacementRepository:
    return InMemoryPlacementRepository()


@pytest.fixture
def copies() -> InMemoryImageCopyExistenceCheck:
    return InMemoryImageCopyExistenceCheck()


@pytest.fixture
def bus() -> RecordingBus:
    return RecordingBus()


@pytest.fixture
def ucs(grids, placements, copies, bus):
    """Bag of all use-case instances sharing the same dependencies."""

    class _Ucs:
        create = CreateGridCanvas(grids, placements, copies, bus)
        change_dims = ChangeGridDimensions(grids, placements, copies, bus)
        change_weights = ChangeRowColumnWeights(grids, placements, copies, bus)
        toggle_lock = ToggleRowColumnLock(grids, placements, copies, bus)
        place = PlaceImageCopy(grids, placements, copies, bus)
        move = MovePlacement(grids, placements, copies, bus)
        swap = SwapPlacements(grids, placements, copies, bus)
        resize = ResizePlacementOccupancy(grids, placements, copies, bus)
        change_order = ChangePlacementOrder(grids, placements, copies, bus)
        remove = RemovePlacement(grids, placements, copies, bus)
        list_p = ListPlacements(grids, placements, copies, bus)

    return _Ucs()


@pytest.fixture
def grid_3x3(ucs):
    return ucs.create.execute("g", 3, 3, PixelSize(900, 900))


def make_copy(copies: InMemoryImageCopyExistenceCheck) -> UUID:
    cid = uuid4()
    copies.register(cid)
    return cid
