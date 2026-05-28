"""RENDERING_EXPORT Anchor tests AT-01..AT-08 (30-design.md §5) + integration.

Implemented verbatim from the worked examples. AT-02..AT-04 fix R-02 (= the
application of IMGVAR R-08). AT-02 catches the forbidden manual+auto synthesis.
All wiring uses the REAL producers through the consumer read ports (no adapter).
"""

import uuid

from grid_composition.domain import CellPosition, OrderOperation
from rendering_export.failures import NotFound
from shared.result import Err, Ok
from shared.value_objects import OccupySize, PixelSize


def _grid(grid, rows=2, cols=2, canvas_w=100, canvas_h=100):
    return grid.create_grid_canvas("g", rows, cols, PixelSize(canvas_w, canvas_h)).unwrap()


def _copy(imgvar):
    asset_id = imgvar.import_image_asset(b"IMG:10x10:x", "x.png", "image/png").unwrap()
    return imgvar.create_image_copy(asset_id).unwrap()


# AT-01: order=[3,1,2] placements -> items ascending placement_order (z). (R-01)
def test_at_01_items_in_placement_order(render, grid, imgvar):
    gid = _grid(grid, rows=1, cols=3, canvas_w=300, canvas_h=100)
    c0 = _copy(imgvar)
    c1 = _copy(imgvar)
    c2 = _copy(imgvar)
    # placed in cells 0,1,2 -> placement_order becomes 1,2,3 in creation order.
    p0 = grid.place_image_copy(gid, c0, CellPosition(0, 0), OccupySize(1, 1)).unwrap()
    p1 = grid.place_image_copy(gid, c1, CellPosition(1, 0), OccupySize(1, 1)).unwrap()
    p2 = grid.place_image_copy(gid, c2, CellPosition(2, 0), OccupySize(1, 1)).unwrap()
    # Shuffle z so creation order != z order: make c2 front, c0 middle, etc.
    grid.change_placement_order(p2, OrderOperation.SetOrder, 1)  # c2 -> order 1
    grid.change_placement_order(p0, OrderOperation.SetOrder, 3)  # c0 -> order 3
    # Now z order should be c2(1), c1(2), c0(3).
    layout = grid.get_grid_layout(gid)
    expected = [pv.copy_id for pv in sorted(layout.placements, key=lambda p: p.order)]
    model = render.build_render_model(gid).unwrap()
    actual = [it.copy_id for it in model.items]
    assert actual == expected
    # explicit: ascending order indices.
    orders = sorted(p.order for p in layout.placements)
    assert orders == [1, 2, 3]


# AT-02: manual+auto both present -> EffectiveCrop kind="manual" (auto ignored). (R-02)
def test_at_02_manual_plus_auto_yields_manual(render, imgvar):
    cid = _copy(imgvar)
    imgvar.change_auto_crop_settings(cid, 0xFFFFFFFF, 10)
    imgvar.change_manual_crop_settings(cid, 0.1, 0.1, 0.5, 0.5)
    crop = render.resolve_effective_crop(cid).unwrap()
    assert crop.kind == "manual"
    assert crop.value == (0.1, 0.1, 0.5, 0.5)


# AT-03: auto only -> kind="auto". (R-02)
def test_at_03_auto_only(render, imgvar):
    cid = _copy(imgvar)
    imgvar.change_auto_crop_settings(cid, 0xFF000000, 5)
    crop = render.resolve_effective_crop(cid).unwrap()
    assert crop.kind == "auto"


# AT-04: both None -> kind="none". (R-02)
def test_at_04_none(render, imgvar):
    cid = _copy(imgvar)
    crop = render.resolve_effective_crop(cid).unwrap()
    assert crop.kind == "none"


# AT-05: 2x2 uniform, canvas 100x100, (0,0,1x1) -> (0,0,50,50). (R-04)
def test_at_05_uniform_pixel_rect(render, grid, imgvar):
    gid = _grid(grid, rows=2, cols=2, canvas_w=100, canvas_h=100)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(0, 0), OccupySize(1, 1))
    item = render.build_render_model(gid).unwrap().items[0]
    assert (item.px, item.py, item.pw, item.ph) == (0, 0, 50, 50)


# AT-05b: (1,1,1x1) -> (50,50,50,50).
def test_at_05b_uniform_pixel_rect_second_cell(render, grid, imgvar):
    gid = _grid(grid, rows=2, cols=2, canvas_w=100, canvas_h=100)
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(1, 1), OccupySize(1, 1))
    item = render.build_render_model(gid).unwrap().items[0]
    assert (item.px, item.py, item.pw, item.ph) == (50, 50, 50, 50)


# AT-06: 1x2 cols weights [1,3], canvas 100 wide, (x=1,1x1) -> px=25,pw=75. (R-04)
def test_at_06_nonuniform_pixel_rect(render, grid, imgvar):
    gid = grid.create_grid_canvas("g", 1, 2, PixelSize(100, 100)).unwrap()
    from grid_composition.domain import Axis
    grid.change_row_column_weights(gid, Axis.Col, (1, 3))
    cid = _copy(imgvar)
    grid.place_image_copy(gid, cid, CellPosition(1, 0), OccupySize(1, 1))
    item = render.build_render_model(gid).unwrap().items[0]
    assert item.px == 25 and item.pw == 75


# AT-07: dangling copy (spec=None) excluded; others remain. (R-03)
def test_at_07_dangling_copy_excluded(render, grid, imgvar):
    gid = _grid(grid, rows=1, cols=2, canvas_w=200, canvas_h=100)
    good = _copy(imgvar)
    dangling = _copy(imgvar)
    grid.place_image_copy(gid, good, CellPosition(0, 0), OccupySize(1, 1))
    grid.place_image_copy(gid, dangling, CellPosition(1, 0), OccupySize(1, 1))
    # delete the copy in IMGVAR so its render spec becomes None (dangling ref).
    imgvar.delete_image_copy(dangling)
    model = render.build_render_model(gid).unwrap()
    ids = [it.copy_id for it in model.items]
    assert good in ids
    assert dangling not in ids
    assert len(model.items) == 1  # excluded, NOT an error


# AT-08: nonexistent grid_id -> NotFound(entity_kind="Grid").
def test_at_08_unknown_grid_notfound(render):
    res = render.build_render_model(uuid.uuid4())
    assert isinstance(res, Err)
    assert isinstance(res.error, NotFound) and res.error.entity_kind == "Grid"


# ---------------------------------------------------------------------------
# Render integration test: placed copies become a z-ordered model and crop is
# resolved per R-08 across manual/auto/none in one grid.
# ---------------------------------------------------------------------------
def test_integration_zorder_and_crop_resolution(render, grid, imgvar):
    gid = _grid(grid, rows=1, cols=3, canvas_w=300, canvas_h=100)
    c_manual = _copy(imgvar)
    c_auto = _copy(imgvar)
    c_none = _copy(imgvar)
    imgvar.change_auto_crop_settings(c_manual, 0x11223344, 7)
    imgvar.change_manual_crop_settings(c_manual, 0.2, 0.2, 0.4, 0.4)  # manual wins
    imgvar.change_auto_crop_settings(c_auto, 0x55667788, 3)           # auto only
    # c_none: neither

    p_m = grid.place_image_copy(gid, c_manual, CellPosition(0, 0), OccupySize(1, 1)).unwrap()
    p_a = grid.place_image_copy(gid, c_auto, CellPosition(1, 0), OccupySize(1, 1)).unwrap()
    p_n = grid.place_image_copy(gid, c_none, CellPosition(2, 0), OccupySize(1, 1)).unwrap()
    # reverse z so order matters: none front, auto mid, manual back.
    grid.change_placement_order(p_n, OrderOperation.SetOrder, 1)
    grid.change_placement_order(p_a, OrderOperation.SetOrder, 2)
    grid.change_placement_order(p_m, OrderOperation.SetOrder, 3)

    model = render.build_render_model(gid).unwrap()
    # z order: c_none, c_auto, c_manual.
    assert [it.copy_id for it in model.items] == [c_none, c_auto, c_manual]
    crop_by_copy = {it.copy_id: it.effective_crop.kind for it in model.items}
    assert crop_by_copy[c_manual] == "manual"
    assert crop_by_copy[c_auto] == "auto"
    assert crop_by_copy[c_none] == "none"
    # geometry: 3 cols uniform over 300 -> each 100 wide.
    px_by_copy = {it.copy_id: (it.px, it.pw) for it in model.items}
    assert px_by_copy[c_none] == (200, 100)   # cell x=2
    assert px_by_copy[c_auto] == (100, 100)   # cell x=1
    assert px_by_copy[c_manual] == (0, 100)   # cell x=0
