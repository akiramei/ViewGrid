"""n=2 compose integration: GRID UC-05 boundary via ImageCopyExistencePort.

Zero adapters: IMGVAR satisfies the Port natively.
"""
from __future__ import annotations

import uuid

from grid_composition.domain import CellPosition
from grid_composition.use_cases import GridCompositionUseCases
from grid_composition import failures as gfail
from image_variant_management.use_cases import ImageVariantManagementUseCases
from shared.events import RecordingBus
from shared.ports import ImageCopyExistencePort
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def build():
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(bus=bus)
    grid = GridCompositionUseCases(image_copy_existence=imgvar, bus=bus)
    return imgvar, grid


def test_imgvar_satisfies_existence_port():
    imgvar = ImageVariantManagementUseCases()
    port: ImageCopyExistencePort = imgvar  # structural typing, no adapter
    assert port.exists(uuid.uuid4()) is False


def test_existing_copy_places_ok():
    imgvar, grid = build()
    asset = imgvar.import_image_asset(b"x", "p.png", "image/png").value
    copy = imgvar.create_image_copy(asset.id).value
    gid = grid.create_grid_canvas("g", 3, 3, PixelSize(300, 300)).value.id
    res = grid.place_image_copy(gid, copy.id, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Ok)


def test_unknown_copy_rejected():
    imgvar, grid = build()
    gid = grid.create_grid_canvas("g", 3, 3, PixelSize(300, 300)).value.id
    res = grid.place_image_copy(gid, uuid.uuid4(), CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Err) and isinstance(res.error, gfail.UnknownCopyId)
