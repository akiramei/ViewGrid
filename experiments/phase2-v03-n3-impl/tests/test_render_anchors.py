"""RENDERING_EXPORT Anchor tests AT-01..AT-08 (30-design.md §5) + json.dumps."""
from __future__ import annotations

import json
import uuid

from grid_composition.domain import CellPosition
from grid_composition.use_cases import GridCompositionUseCases
from image_variant_management.use_cases import ImageVariantManagementUseCases
from rendering_export import failures as fail
from rendering_export.use_cases import RenderingExportUseCases
from shared.events import RecordingBus
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def build():
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(bus=bus)
    grid = GridCompositionUseCases(image_copy_existence=imgvar, bus=bus)
    render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)
    return imgvar, grid, render


def make_copy(imgvar):
    asset = imgvar.import_image_asset(uuid.uuid4().bytes, "p.png", "image/png").value
    return imgvar.create_image_copy(asset.id).value.id


# AT-01: order=[3,1,2] -> items ascending by order ----------------------
def test_at_01_render_order_follows_placement_order():
    imgvar, grid, render = build()
    gid = grid.create_grid_canvas("g", 1, 3, PixelSize(300, 100)).value.id
    c0 = make_copy(imgvar)
    c1 = make_copy(imgvar)
    c2 = make_copy(imgvar)
    p0 = grid.place_image_copy(gid, c0, CellPosition(0, 0), OccupySize(1, 1)).value  # order 1
    p1 = grid.place_image_copy(gid, c1, CellPosition(1, 0), OccupySize(1, 1)).value  # order 2
    p2 = grid.place_image_copy(gid, c2, CellPosition(2, 0), OccupySize(1, 1)).value  # order 3
    # shuffle orders to [3,1,2] for c0,c1,c2
    from grid_composition.enums import OrderOperation

    grid.change_placement_order(p0.id, OrderOperation.SetOrder, order_value=3)
    layout = grid.get_grid_layout(gid)
    expected = [pv.copy_id for pv in sorted(layout.placements, key=lambda p: p.order)]
    model = render.build_render_model(gid).value
    assert [it.copy_id for it in model.items] == expected
    # explicit ascending check
    orders = [pv.order for pv in sorted(layout.placements, key=lambda p: p.order)]
    assert orders == sorted(orders)


# AT-02: manual + auto -> manual (auto ignored) -------------------------
def test_at_02_manual_overrides_auto():
    imgvar, grid, render = build()
    copy_id = make_copy(imgvar)
    imgvar.change_auto_crop_settings(copy_id, 0xFFFFFFFF, 10)
    imgvar.change_manual_crop_settings(copy_id, 0.1, 0.1, 0.5, 0.5)
    res = render.resolve_effective_crop(copy_id)
    assert isinstance(res, Ok)
    assert res.value.kind == "manual"
    assert res.value.value == (0.1, 0.1, 0.5, 0.5)


# AT-03: auto only -> auto ----------------------------------------------
def test_at_03_auto_only():
    imgvar, grid, render = build()
    copy_id = make_copy(imgvar)
    imgvar.change_auto_crop_settings(copy_id, 0xFF000000, 5)
    res = render.resolve_effective_crop(copy_id)
    assert isinstance(res, Ok) and res.value.kind == "auto"


# AT-04: both None -> none ----------------------------------------------
def test_at_04_none():
    imgvar, grid, render = build()
    copy_id = make_copy(imgvar)
    res = render.resolve_effective_crop(copy_id)
    assert isinstance(res, Ok) and res.value.kind == "none"


# AT-05: 2x2 uniform, canvas 100x100, (0,0,1x1) -> (0,0,50,50) -----------
def test_at_05_pixel_rect_uniform():
    imgvar, grid, render = build()
    gid = grid.create_grid_canvas("g", 2, 2, PixelSize(100, 100)).value.id
    copy_id = make_copy(imgvar)
    grid.place_image_copy(gid, copy_id, CellPosition(0, 0), OccupySize(1, 1))
    model = render.build_render_model(gid).value
    it = model.items[0]
    assert (it.px, it.py, it.pw, it.ph) == (0, 0, 50, 50)


# AT-06: 1x2 cols weights [1,3], canvas width 100, (x=1,1x1) -> px=25,pw=75 -
def test_at_06_pixel_rect_non_uniform():
    imgvar, grid, render = build()
    gid = grid.create_grid_canvas("g", 1, 2, PixelSize(100, 50)).value.id
    grid.change_row_column_weights(gid, __import__("grid_composition.enums", fromlist=["Axis"]).Axis.Col, (1, 3))
    copy_id = make_copy(imgvar)
    grid.place_image_copy(gid, copy_id, CellPosition(1, 0), OccupySize(1, 1))
    model = render.build_render_model(gid).value
    it = model.items[0]
    assert it.px == 25 and it.pw == 75


# AT-07: dangling copy (spec None) excluded, others remain --------------
def test_at_07_dangling_copy_excluded():
    imgvar, grid, render = build()
    gid = grid.create_grid_canvas("g", 1, 2, PixelSize(200, 100)).value.id
    good = make_copy(imgvar)
    dangling = make_copy(imgvar)
    grid.place_image_copy(gid, good, CellPosition(0, 0), OccupySize(1, 1))
    grid.place_image_copy(gid, dangling, CellPosition(1, 0), OccupySize(1, 1))
    # delete the copy so its render spec becomes None (placement still references it)
    imgvar.delete_image_copy(dangling)
    model = render.build_render_model(gid).value
    assert len(model.items) == 1
    assert model.items[0].copy_id == good


# AT-08: unknown grid -> NotFound(entity_kind="Grid") -------------------
def test_at_08_unknown_grid():
    _, _, render = build()
    res = render.build_render_model(uuid.uuid4())
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "Grid"


# C-IDENTITY-BOUNDARY: json.dumps(RenderDescriptor) succeeds ------------
def test_render_descriptor_json_dumps_succeeds():
    imgvar, grid, render = build()
    gid = grid.create_grid_canvas("g", 2, 2, PixelSize(100, 100)).value.id
    copy_id = make_copy(imgvar)
    imgvar.change_manual_crop_settings(copy_id, 0.1, 0.1, 0.5, 0.5)
    grid.place_image_copy(gid, copy_id, CellPosition(0, 0), OccupySize(1, 1))
    descriptor = render.export_render_descriptor(gid).value
    # Should not raise: identities are str at the output boundary.
    blob = json.dumps(descriptor.to_dict())
    parsed = json.loads(blob)
    assert parsed["grid_id"] == str(gid)
    assert isinstance(parsed["items"][0]["copy_id"], str)
    assert parsed["items"][0]["copy_id"] == str(copy_id)
