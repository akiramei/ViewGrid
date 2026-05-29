"""RENDERING_EXPORT UC happy/failure + Event tests.

Wires the REAL producers (GRID + IMGVAR) into RENDERING via the shared
read ports with ZERO adapters -- proving the consumer plugs straight into
the pre-loaded projections.
"""
from __future__ import annotations

import uuid

from grid_composition.domain import CellPosition
from grid_composition.use_cases import GridCompositionUseCases
from image_variant_management.use_cases import ImageVariantManagementUseCases
from rendering_export import events as ev
from rendering_export import failures as fail
from rendering_export.use_cases import RenderingExportUseCases
from shared.events import RecordingBus
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def build():
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(bus=bus)
    grid = GridCompositionUseCases(image_copy_existence=imgvar, bus=bus)
    # No adapter: producers already satisfy the read ports.
    render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)
    return imgvar, grid, render, bus


def make_grid_with_copy(imgvar, grid, rows=2, cols=2, canvas=(100, 100)):
    asset = imgvar.import_image_asset(b"x", "p.png", "image/png").value
    copy = imgvar.create_image_copy(asset.id).value
    gid = grid.create_grid_canvas("g", rows, cols, PixelSize(*canvas)).value.id
    grid.place_image_copy(gid, copy.id, CellPosition(0, 0), OccupySize(1, 1))
    return gid, copy.id


# ---------------------------------------------------------------- happy
def test_uc01_build_render_model():
    imgvar, grid, render, _ = build()
    gid, copy_id = make_grid_with_copy(imgvar, grid)
    res = render.build_render_model(gid)
    assert isinstance(res, Ok)
    assert len(res.value.items) == 1
    item = res.value.items[0]
    assert item.copy_id == copy_id
    assert (item.px, item.py, item.pw, item.ph) == (0, 0, 50, 50)


def test_uc02_resolve_effective_crop():
    imgvar, grid, render, _ = build()
    gid, copy_id = make_grid_with_copy(imgvar, grid)
    res = render.resolve_effective_crop(copy_id)
    assert isinstance(res, Ok) and res.value.kind == "none"


def test_uc03_export_descriptor():
    imgvar, grid, render, _ = build()
    gid, _ = make_grid_with_copy(imgvar, grid)
    res = render.export_render_descriptor(gid)
    assert isinstance(res, Ok)
    assert isinstance(res.value.grid_id, str)  # C-IDENTITY-BOUNDARY


# ---------------------------------------------------------------- failure
def test_uc01_grid_not_found():
    _, _, render, _ = build()
    res = render.build_render_model(uuid.uuid4())
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "Grid"


def test_uc02_copy_not_found():
    _, _, render, _ = build()
    res = render.resolve_effective_crop(uuid.uuid4())
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "ImageCopy"


def test_uc03_grid_not_found():
    _, _, render, _ = build()
    res = render.export_render_descriptor(uuid.uuid4())
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)


# ---------------------------------------------------------------- events
def test_event_render_model_built_on_success_only():
    imgvar, grid, render, bus = build()
    gid, _ = make_grid_with_copy(imgvar, grid)
    bus.clear()
    render.build_render_model(gid)
    assert len(bus.of_type(ev.RenderModelBuilt)) == 1


def test_event_not_emitted_on_failure():
    _, _, render, bus = build()
    bus.clear()
    render.build_render_model(uuid.uuid4())
    assert len(bus.of_type(ev.RenderModelBuilt)) == 0


def test_event_descriptor_exported():
    imgvar, grid, render, bus = build()
    gid, _ = make_grid_with_copy(imgvar, grid)
    bus.clear()
    render.export_render_descriptor(gid)
    assert len(bus.of_type(ev.RenderDescriptorExported)) == 1
