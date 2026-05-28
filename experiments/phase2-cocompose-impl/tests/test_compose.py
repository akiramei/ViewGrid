"""Compose integration tests (THE core experiment).

These wire ImageVariantManagementUseCases DIRECTLY into GridCompositionUseCases
as the ImageCopyExistencePort. There is NO hand-written adapter anywhere in this
file or in the production code path -- imgvar.exists(uuid)->bool IS the Port.
"""

import uuid

from grid_composition.domain import CellPosition
from grid_composition.failures import UnknownCopyId
from shared.ports import ImageCopyExistencePort
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def test_imgvar_satisfies_port_without_adapter(imgvar):
    # Runtime-checkable Protocol: the UseCases object IS the Port.
    assert isinstance(imgvar, ImageCopyExistencePort)


def test_compose_place_existing_copy_succeeds(grid, imgvar):
    # IMAGE_VARIANT creates a real ImageCopy ...
    asset_id = imgvar.import_image_asset(b"IMG:5x5:x", "x.png", "image/png").unwrap()
    copy_id = imgvar.create_image_copy(asset_id, copy_name="c").unwrap()

    gid = grid.create_grid_canvas("g", 3, 3, PixelSize(300, 300)).unwrap()

    # ... GRID UC-05 places that CopyId successfully (Port returns True).
    res = grid.place_image_copy(gid, copy_id, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Ok)
    placed = grid.list_placements(gid)[0]
    assert placed.copy_id == copy_id


def test_compose_place_unknown_copy_returns_unknown_copy_id(grid, imgvar):
    gid = grid.create_grid_canvas("g", 3, 3, PixelSize(300, 300)).unwrap()
    unknown = uuid.uuid4()  # never created in IMAGE_VARIANT
    res = grid.place_image_copy(gid, unknown, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Err)
    assert isinstance(res.error, UnknownCopyId)
    assert res.error.copy_id == unknown


def test_compose_copy_id_is_uuid_not_str(grid, imgvar):
    # C-IDENTITY: identity stays uuid.UUID end-to-end; no str conversion needed.
    asset_id = imgvar.import_image_asset(b"IMG:5x5:y", "y.png", "image/png").unwrap()
    copy_id = imgvar.create_image_copy(asset_id).unwrap()
    assert isinstance(copy_id, uuid.UUID)
    gid = grid.create_grid_canvas("g", 2, 2, PixelSize(200, 200)).unwrap()
    res = grid.place_image_copy(gid, copy_id, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(res, Ok)
