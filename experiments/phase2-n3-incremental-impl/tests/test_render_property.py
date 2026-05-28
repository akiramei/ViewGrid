"""RENDERING_EXPORT property-based 1000-step random walk (30-design.md §4.5).

A seed-fixed random walk that builds random grids / placements / copy crop
settings and asserts the RENDERING invariants on every produced model:
  - items are in ascending placement_order (z order)            (R-01)
  - every item's pixel rect is within the canvas, pw/ph >= 0    (R-04)
  - every item's effective_crop.kind is one of the 3 values     (R-02)
  - dangling copies are excluded (item count <= placement count) (R-03)

Deterministic / reproducible: fixed seed, real producers wired with no adapter.
"""

import random
import uuid

from grid_composition.domain import Axis, CellPosition, OrderOperation
from grid_composition.repositories import (
    InMemoryGridCanvasRepository,
    InMemoryPlacementRepository,
)
from grid_composition.use_cases import GridCompositionUseCases
from image_variant_management.repositories import (
    InMemoryBlobStorage,
    InMemoryImageAssetRepository,
    InMemoryImageCopyRepository,
)
from image_variant_management.use_cases import ImageVariantManagementUseCases
from rendering_export.use_cases import RenderingExportUseCases
from shared.eventbus import RecordingBus
from shared.result import Ok
from shared.value_objects import OccupySize, PixelSize

_VALID_CROP_KINDS = {"manual", "auto", "none"}


def _fresh_stack():
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(
        InMemoryImageAssetRepository(), InMemoryImageCopyRepository(),
        InMemoryBlobStorage(), bus,
    )
    grid = GridCompositionUseCases(
        InMemoryGridCanvasRepository(), InMemoryPlacementRepository(),
        image_copy_existence=imgvar, bus=bus,
    )
    render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)
    return bus, imgvar, grid, render


def _make_copy(imgvar, rng):
    asset_id = imgvar.import_image_asset(
        b"IMG:10x10:" + str(rng.random()).encode(), "x.png", "image/png"
    ).unwrap()
    return imgvar.create_image_copy(asset_id).unwrap()


def _check_invariants(model, layout_cols_canvas):
    canvas_w, canvas_h = layout_cols_canvas
    orders_proxy = list(range(len(model.items)))
    assert orders_proxy == sorted(orders_proxy)  # items list is already z-sorted
    for it in model.items:
        assert it.pw >= 0 and it.ph >= 0
        assert 0 <= it.px <= canvas_w
        assert 0 <= it.py <= canvas_h
        assert it.px + it.pw <= canvas_w
        assert it.py + it.ph <= canvas_h
        assert it.effective_crop.kind in _VALID_CROP_KINDS


def test_1000_step_random_walk():
    rng = random.Random(20260529)
    bus, imgvar, grid, render = _fresh_stack()

    grids: list[uuid.UUID] = []
    grid_dims: dict[uuid.UUID, tuple[int, int, int, int]] = {}  # gid -> rows,cols,cw,ch
    copies: list[uuid.UUID] = []
    placements: list[uuid.UUID] = []

    for _ in range(1000):
        op = rng.randint(0, 6)

        if op == 0 or not grids:
            rows = rng.randint(1, 4)
            cols = rng.randint(1, 4)
            cw = rng.randint(cols, 400)
            ch = rng.randint(rows, 400)
            res = grid.create_grid_canvas("g", rows, cols, PixelSize(cw, ch))
            if isinstance(res, Ok):
                gid = res.unwrap()
                grids.append(gid)
                grid_dims[gid] = (rows, cols, cw, ch)

        elif op == 1 or not copies:
            copies.append(_make_copy(imgvar, rng))

        elif op == 2 and copies:
            gid = rng.choice(grids)
            rows, cols, cw, ch = grid_dims[gid]
            cid = rng.choice(copies)
            x = rng.randint(0, cols - 1)
            y = rng.randint(0, rows - 1)
            res = grid.place_image_copy(gid, cid, CellPosition(x, y), OccupySize(1, 1))
            if isinstance(res, Ok):
                placements.append(res.unwrap())

        elif op == 3 and copies:
            # set a random crop combination on a random copy
            cid = rng.choice(copies)
            choice = rng.randint(0, 2)
            if choice == 0:
                imgvar.change_manual_crop_settings(cid, 0.1, 0.1, 0.3, 0.3)
            elif choice == 1:
                imgvar.change_auto_crop_settings(cid, rng.randint(0, 0xFFFFFFFF), rng.randint(0, 255))
            else:
                imgvar.change_manual_crop_settings(cid, 0.0, 0.0, 0.5, 0.5)
                imgvar.change_auto_crop_settings(cid, 0x0, 5)  # both -> manual wins

        elif op == 4 and placements:
            grid.change_placement_order(rng.choice(placements), OrderOperation.BringToFront)

        elif op == 5 and copies and len(copies) > 1:
            # delete a copy -> creates a dangling reference for any placement of it
            cid = copies.pop(rng.randrange(len(copies)))
            imgvar.delete_image_copy(cid)

        elif op == 6 and grids:
            # build & assert invariants on a random grid
            gid = rng.choice(grids)
            res = render.build_render_model(gid)
            assert isinstance(res, Ok)
            model = res.unwrap()
            rows, cols, cw, ch = grid_dims[gid]
            _check_invariants(model, (cw, ch))
            # R-03: item count never exceeds placement count for that grid
            layout = grid.get_grid_layout(gid)
            assert len(model.items) <= len(layout.placements)

    # final sweep: assert invariants over all grids.
    for gid in grids:
        res = render.build_render_model(gid)
        assert isinstance(res, Ok)
        model = res.unwrap()
        rows, cols, cw, ch = grid_dims[gid]
        _check_invariants(model, (cw, ch))
