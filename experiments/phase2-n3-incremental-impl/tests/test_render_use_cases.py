"""RENDERING_EXPORT UseCase happy/failure + Event tests (30-design.md §4.2-4.3).

All wiring uses the REAL producers (grid + imgvar) injected as the consumer read
ports with zero adapter code (see conftest.render fixture).
"""

import uuid

import rendering_export.events as rev
from grid_composition.domain import CellPosition
from rendering_export.domain import RenderModel
from rendering_export.failures import NotFound
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def _grid_2x2(grid, canvas=100):
    return grid.create_grid_canvas("g", 2, 2, PixelSize(canvas, canvas)).unwrap()


def _copy(imgvar, **crop):
    asset_id = imgvar.import_image_asset(b"IMG:10x10:x", "x.png", "image/png").unwrap()
    cid = imgvar.create_image_copy(asset_id).unwrap()
    return cid


# ============================ UC-01 BuildRenderModel ========================
def test_uc01_happy_returns_render_model(render, grid, imgvar):
    gid = _grid_2x2(grid)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    res = render.build_render_model(gid)
    assert isinstance(res, Ok)
    model = res.unwrap()
    assert isinstance(model, RenderModel)
    assert model.grid_id == gid
    assert model.canvas_w == 100 and model.canvas_h == 100
    assert len(model.items) == 1
    assert model.items[0].copy_id == cid


def test_uc01_unknown_grid_is_notfound_grid(render):
    res = render.build_render_model(uuid.uuid4())
    assert isinstance(res, Err)
    assert isinstance(res.error, NotFound)
    assert res.error.entity_kind == "Grid"


def test_uc01_empty_grid_returns_empty_items(render, grid):
    gid = _grid_2x2(grid)
    model = render.build_render_model(gid).unwrap()
    assert model.items == ()


# ============================ UC-02 ResolveEffectiveCrop ====================
def test_uc02_happy_manual(render, imgvar):
    cid = _copy(imgvar)
    imgvar.change_auto_crop_settings(cid, 0xFFFFFFFF, 10)
    imgvar.change_manual_crop_settings(cid, 0.1, 0.1, 0.5, 0.5)
    res = render.resolve_effective_crop(cid)
    assert isinstance(res, Ok)
    assert res.unwrap().kind == "manual"


def test_uc02_happy_auto(render, imgvar):
    cid = _copy(imgvar)
    imgvar.change_auto_crop_settings(cid, 0xFF000000, 5)
    res = render.resolve_effective_crop(cid)
    assert res.unwrap().kind == "auto"


def test_uc02_happy_none(render, imgvar):
    cid = _copy(imgvar)
    res = render.resolve_effective_crop(cid)
    assert res.unwrap().kind == "none"


def test_uc02_unknown_copy_is_notfound_imagecopy(render):
    res = render.resolve_effective_crop(uuid.uuid4())
    assert isinstance(res, Err)
    assert isinstance(res.error, NotFound)
    assert res.error.entity_kind == "ImageCopy"


# ============================ UC-03 ExportRenderDescriptor ==================
def test_uc03_happy_returns_dict(render, grid, imgvar):
    gid = _grid_2x2(grid)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    res = render.export_render_descriptor(gid)
    assert isinstance(res, Ok)
    desc = res.unwrap()
    assert isinstance(desc, dict)
    assert desc["grid_id"] == gid
    assert len(desc["items"]) == 1
    assert desc["items"][0]["copy_id"] == cid
    # descriptor is serializable-shaped (nested dicts/lists/primitives).
    assert desc["items"][0]["effective_crop"]["kind"] == "none"


def test_uc03_unknown_grid_is_notfound(render):
    res = render.export_render_descriptor(uuid.uuid4())
    assert isinstance(res, Err)
    assert res.error.entity_kind == "Grid"


def test_uc03_descriptor_is_deterministic(render, grid, imgvar):
    gid = _grid_2x2(grid)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    d1 = render.export_render_descriptor(gid).unwrap()
    d2 = render.export_render_descriptor(gid).unwrap()
    assert d1 == d2


# ============================ Events ========================================
def test_event_render_model_built_on_success_only(render, grid, imgvar, bus):
    gid = _grid_2x2(grid)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    bus.clear()
    render.build_render_model(gid)
    built = bus.of_type(rev.RenderModelBuilt)
    assert len(built) == 1
    assert built[0].grid_id == gid and built[0].item_count == 1


def test_event_not_emitted_on_notfound(render, bus):
    bus.clear()
    render.build_render_model(uuid.uuid4())
    assert bus.of_type(rev.RenderModelBuilt) == []


def test_event_descriptor_exported_on_success_only(render, grid, imgvar, bus):
    gid = _grid_2x2(grid)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    bus.clear()
    render.export_render_descriptor(gid)
    exported = bus.of_type(rev.RenderDescriptorExported)
    assert len(exported) == 1
    assert exported[0].grid_id == gid and exported[0].item_count == 1


def test_uc03_emits_only_descriptor_event_not_model_built(render, grid, imgvar, bus):
    gid = _grid_2x2(grid)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    bus.clear()
    render.export_render_descriptor(gid)
    # UC-03 emits RenderDescriptorExported; it must NOT also emit RenderModelBuilt.
    assert len(bus.of_type(rev.RenderDescriptorExported)) == 1
    assert bus.of_type(rev.RenderModelBuilt) == []


def test_descriptor_event_not_emitted_on_notfound(render, bus):
    bus.clear()
    render.export_render_descriptor(uuid.uuid4())
    assert bus.of_type(rev.RenderDescriptorExported) == []
