"""Pre-loaded read-port tests (C-CONSUMER-PORTS v0.3).

Confirms n=2 producers already return the neutral DTOs from the start,
even though no consumer (RENDERING) exists yet.
"""
from __future__ import annotations

import uuid

from grid_composition.domain import CellPosition
from grid_composition.use_cases import GridCompositionUseCases
from image_variant_management.enums import Alignment, Rotation, ScalingMode
from image_variant_management.domain import ImageTransform
from image_variant_management.use_cases import ImageVariantManagementUseCases
from shared.ports import CopyRenderSpecPort, GridLayoutPort
from shared.render_contracts import CopyRenderSpec, GridLayout, PlacementView
from shared.result import Ok
from shared.value_objects import OccupySize, PixelSize


def test_get_grid_layout_returns_neutral_dto():
    imgvar = ImageVariantManagementUseCases()
    grid = GridCompositionUseCases(image_copy_existence=imgvar)
    gid = grid.create_grid_canvas("g", 2, 2, PixelSize(100, 100)).value.id
    asset = imgvar.import_image_asset(b"x", "p.png", "image/png").value
    copy = imgvar.create_image_copy(asset.id).value
    grid.place_image_copy(gid, copy.id, CellPosition(0, 0), OccupySize(1, 1))

    layout = grid.get_grid_layout(gid)
    assert isinstance(layout, GridLayout)
    assert layout.grid_rows == 2 and layout.grid_cols == 2
    assert layout.canvas_w == 100 and layout.canvas_h == 100
    assert len(layout.placements) == 1
    pv = layout.placements[0]
    assert isinstance(pv, PlacementView)
    assert pv.copy_id == copy.id and pv.order == 1


def test_get_grid_layout_none_for_missing():
    grid = GridCompositionUseCases()
    assert grid.get_grid_layout(uuid.uuid4()) is None


def test_get_copy_render_spec_returns_neutral_dto():
    imgvar = ImageVariantManagementUseCases()
    asset = imgvar.import_image_asset(b"x", "p.png", "image/png").value
    copy = imgvar.create_image_copy(
        asset.id,
        initial_transform=ImageTransform(Rotation.CW90, flip_x=True, flip_y=False),
        initial_scaling_mode=ScalingMode.Fill,
        initial_alignment=Alignment.TopLeft,
    ).value
    imgvar.change_auto_crop_settings(copy.id, 0xFFFFFFFF, 8)
    imgvar.change_manual_crop_settings(copy.id, 0.1, 0.1, 0.5, 0.5)

    spec = imgvar.get_copy_render_spec(copy.id)
    assert isinstance(spec, CopyRenderSpec)
    # enums are projected to neutral str (no producer enum types leak).
    assert spec.rotation == "CW90"
    assert isinstance(spec.rotation, str)
    assert spec.scaling_mode == "Fill"
    assert spec.alignment == "TopLeft"
    assert spec.flip_x is True and spec.flip_y is False
    assert spec.auto_crop == (0xFFFFFFFF, 8)
    assert spec.manual_crop == (0.1, 0.1, 0.5, 0.5)


def test_get_copy_render_spec_none_for_missing():
    imgvar = ImageVariantManagementUseCases()
    assert imgvar.get_copy_render_spec(uuid.uuid4()) is None


def test_producers_satisfy_ports_structurally():
    # Pre-loaded ports are satisfied natively (Protocol structural typing).
    imgvar = ImageVariantManagementUseCases()
    grid = GridCompositionUseCases()
    gl: GridLayoutPort = grid
    cs: CopyRenderSpecPort = imgvar
    assert gl.get_grid_layout(uuid.uuid4()) is None
    assert cs.get_copy_render_spec(uuid.uuid4()) is None
