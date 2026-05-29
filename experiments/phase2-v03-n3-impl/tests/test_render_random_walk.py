"""RENDERING_EXPORT 1000-step random walk (30-design.md §4.5).

Random grids/placements/copy settings; invariants checked each step:
  - items are z-order ascending
  - every item px,py,pw,ph >= 0 and within the canvas
  - crop kind is one of the 3 allowed values
seed-fixed for reproducibility.
"""
from __future__ import annotations

import random
import uuid

from grid_composition.domain import CellPosition
from grid_composition.enums import Axis
from grid_composition.use_cases import GridCompositionUseCases
from image_variant_management.use_cases import ImageVariantManagementUseCases
from rendering_export.use_cases import RenderingExportUseCases
from shared.events import RecordingBus
from shared.result import Ok
from shared.value_objects import OccupySize, PixelSize


def test_render_random_walk_1000_steps():
    rng = random.Random(20260529)
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(bus=bus)
    grid = GridCompositionUseCases(image_copy_existence=imgvar, bus=bus)
    render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)

    rows, cols = 3, 3
    canvas_w, canvas_h = 90, 60
    gid = grid.create_grid_canvas("rw", rows, cols, PixelSize(canvas_w, canvas_h)).value.id
    asset = imgvar.import_image_asset(b"seed", "s.png", "image/png").value
    copy_ids: list[uuid.UUID] = []
    placement_ids: list[uuid.UUID] = []

    for _ in range(1000):
        op = rng.choice(
            ["new_copy", "place", "move", "remove", "autocrop", "manualcrop", "weights", "delete_copy"]
        )
        if op == "new_copy":
            r = imgvar.create_image_copy(asset.id)
            if isinstance(r, Ok):
                copy_ids.append(r.value.id)
        elif op == "place" and copy_ids:
            pos = CellPosition(rng.randrange(cols), rng.randrange(rows))
            r = grid.place_image_copy(gid, rng.choice(copy_ids), pos, OccupySize(1, 1))
            if isinstance(r, Ok):
                placement_ids.append(r.value.id)
        elif op == "move" and placement_ids:
            grid.move_placement(
                rng.choice(placement_ids), CellPosition(rng.randrange(cols), rng.randrange(rows))
            )
        elif op == "remove" and placement_ids:
            pid = rng.choice(placement_ids)
            if isinstance(grid.remove_placement(pid), Ok):
                placement_ids.remove(pid)
        elif op == "autocrop" and copy_ids:
            imgvar.change_auto_crop_settings(rng.choice(copy_ids), 0xFF00FF00, rng.randint(0, 255))
        elif op == "manualcrop" and copy_ids:
            imgvar.change_manual_crop_settings(rng.choice(copy_ids), 0.0, 0.0, 0.5, 0.5)
        elif op == "weights":
            axis = rng.choice([Axis.Row, Axis.Col])
            length = cols if axis is Axis.Col else rows
            grid.change_row_column_weights(gid, axis, tuple(rng.randint(1, 4) for _ in range(length)))
        elif op == "delete_copy" and copy_ids:
            cid = rng.choice(copy_ids)
            if isinstance(imgvar.delete_image_copy(cid), Ok):
                copy_ids.remove(cid)

        model_res = render.build_render_model(gid)
        assert isinstance(model_res, Ok)
        model = model_res.value
        # R-01: items follow placement_order ascending, after R-03 exclusion of
        # dangling copies. Compare item copy_ids against the producer's ordered
        # placements whose copy still resolves (note: a copy may back several
        # placements, so we match positionally over the resolvable subset).
        layout = grid.get_grid_layout(gid)
        live_copies = {c.id for c in imgvar.list_image_copies()}
        expected = [
            pv.copy_id
            for pv in sorted(layout.placements, key=lambda p: p.order)
            if pv.copy_id in live_copies
        ]
        assert [it.copy_id for it in model.items] == expected
        # geometry within canvas.
        for it in model.items:
            assert it.px >= 0 and it.py >= 0
            assert it.pw >= 0 and it.ph >= 0
            assert it.px + it.pw <= canvas_w
            assert it.py + it.ph <= canvas_h
            assert it.effective_crop.kind in ("manual", "auto", "none")
