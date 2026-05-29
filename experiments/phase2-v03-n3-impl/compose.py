"""compose.py (Phase B, n=3): wire GRID + IMGVAR + RENDERING_EXPORT.

The n=2 boundary is unchanged (IMGVAR satisfies ImageCopyExistencePort
natively). RENDERING_EXPORT is added as a *consumer* with ZERO adapters:

    render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar)

`grid` already satisfies GridLayoutPort (get_grid_layout) and `imgvar`
already satisfies CopyRenderSpecPort (get_copy_render_spec) -- both were
pre-loaded in Phase A. No producer code was touched to add RENDERING.
"""
from __future__ import annotations

import json
import os
import sys
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from grid_composition.domain import CellPosition  # noqa: E402
from grid_composition.use_cases import GridCompositionUseCases  # noqa: E402
from image_variant_management.use_cases import ImageVariantManagementUseCases  # noqa: E402
from rendering_export.use_cases import RenderingExportUseCases  # noqa: E402
from shared.events import RecordingBus  # noqa: E402
from shared.result import Ok  # noqa: E402
from shared.value_objects import OccupySize, PixelSize  # noqa: E402


def main() -> None:
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(bus=bus)
    # IMGVAR satisfies ImageCopyExistencePort natively -> no adapter.
    grid = GridCompositionUseCases(image_copy_existence=imgvar, bus=bus)
    # RENDERING consumes the PRE-LOADED read ports -> no adapter (1 wiring line).
    render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)

    # IMGVAR: asset + copy with both crops set (R-08 application is RENDERING's job).
    asset_res = imgvar.import_image_asset(b"hello-image-bytes", "photo.png", "image/png")
    assert isinstance(asset_res, Ok)
    copy_id = imgvar.create_image_copy(asset_res.value.id, copy_name="full").value.id
    imgvar.change_auto_crop_settings(copy_id, 0xFFFFFFFF, 8)
    imgvar.change_manual_crop_settings(copy_id, 0.1, 0.1, 0.5, 0.5)

    # GRID: a grid with the placed copy.
    grid_id = grid.create_grid_canvas("demo", 2, 2, PixelSize(100, 100)).value.id
    placed = grid.place_image_copy(grid_id, copy_id, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(placed, Ok), placed
    print("[n=3] n=2 boundary intact (existing copy placed)")

    # Unknown copy still rejected.
    unknown = grid.place_image_copy(grid_id, uuid.uuid4(), CellPosition(1, 1), OccupySize(1, 1))
    assert type(unknown.error).__name__ == "UnknownCopyId"
    print("[n=3] unknown copy rejected with UnknownCopyId")

    # RENDERING reads both producers via the shared ports (no adapter).
    model = render.build_render_model(grid_id)
    assert isinstance(model, Ok), model
    print(f"[n=3] RenderModel built: {len(model.value.items)} item(s), z-order applied")

    crop = render.resolve_effective_crop(copy_id)
    assert isinstance(crop, Ok) and crop.value.kind == "manual"  # R-08 applied: manual wins
    print(f"[n=3] EffectiveCrop kind={crop.value.kind} (R-08 applied: manual overrides auto)")

    descriptor = render.export_render_descriptor(grid_id)
    assert isinstance(descriptor, Ok)
    # C-IDENTITY-BOUNDARY: identities are str -> json.dumps succeeds.
    serialized = json.dumps(descriptor.value.to_dict())
    print(f"[n=3] RenderDescriptor json.dumps len={len(serialized)}")
    print("compose (n=3) OK")


if __name__ == "__main__":
    main()
