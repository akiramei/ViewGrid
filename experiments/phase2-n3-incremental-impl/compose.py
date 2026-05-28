"""Compose demo: wire GRID_COMPOSITION <-> IMAGE_VARIANT_MANAGEMENT <-> RENDERING.

Run:  python experiments/phase2-n3-incremental-impl/compose.py

This script proves the n=3 contract claim (00-convention-contract.md §4.2):

  - IMAGE_VARIANT is passed DIRECTLY into GRID as the ImageCopyExistencePort
    (n=2 boundary, no adapter).
  - GRID is passed DIRECTLY into RENDERING as the GridLayoutPort, and IMAGE_VARIANT
    is passed DIRECTLY into RENDERING as the CopyRenderSpecPort (n=3 read boundary,
    no adapter). Both ports are satisfied natively via projection methods
    (get_grid_layout / get_copy_render_spec).

There is NO adapter class anywhere on any of the three boundaries.
"""

from __future__ import annotations

import os
import sys

# Allow `python experiments/phase2-n3-incremental-impl/compose.py` from any cwd.
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from grid_composition.domain import CellPosition  # noqa: E402
from grid_composition.failures import UnknownCopyId  # noqa: E402
from grid_composition.repositories import (  # noqa: E402
    InMemoryGridCanvasRepository,
    InMemoryPlacementRepository,
)
from grid_composition.use_cases import GridCompositionUseCases  # noqa: E402
from image_variant_management.repositories import (  # noqa: E402
    InMemoryBlobStorage,
    InMemoryImageAssetRepository,
    InMemoryImageCopyRepository,
)
from image_variant_management.use_cases import ImageVariantManagementUseCases  # noqa: E402
from rendering_export.use_cases import RenderingExportUseCases  # noqa: E402
from shared.eventbus import RecordingBus  # noqa: E402
from shared.ports import (  # noqa: E402
    CopyRenderSpecPort,
    GridLayoutPort,
    ImageCopyExistencePort,
)
from shared.result import Ok  # noqa: E402
from shared.value_objects import OccupySize, PixelSize  # noqa: E402


def build():
    bus = RecordingBus()  # one shared synchronous bus (C-EVENTBUS)

    imgvar = ImageVariantManagementUseCases(
        asset_repo=InMemoryImageAssetRepository(),
        copy_repo=InMemoryImageCopyRepository(),
        blob_storage=InMemoryBlobStorage(),
        bus=bus,
    )

    grid = GridCompositionUseCases(
        grid_repo=InMemoryGridCanvasRepository(),
        placement_repo=InMemoryPlacementRepository(),
        # *** NO ADAPTER *** imgvar IS the ImageCopyExistencePort.
        image_copy_existence=imgvar,
        bus=bus,
    )

    render = RenderingExportUseCases(
        # *** NO ADAPTER *** grid IS the GridLayoutPort; imgvar IS the
        # CopyRenderSpecPort. Both satisfied natively by projection methods.
        grid_layout=grid,
        copy_render_spec=imgvar,
        bus=bus,
    )
    return bus, imgvar, grid, render


def main() -> int:
    import uuid

    bus, imgvar, grid, render = build()

    # Runtime proof that the producers satisfy the ports without any wrapper.
    assert isinstance(imgvar, ImageCopyExistencePort)
    assert isinstance(grid, GridLayoutPort), "grid must satisfy GridLayoutPort"
    assert isinstance(imgvar, CopyRenderSpecPort), "imgvar must satisfy CopyRenderSpecPort"
    print("imgvar isinstance ImageCopyExistencePort:", isinstance(imgvar, ImageCopyExistencePort))
    print("grid   isinstance GridLayoutPort        :", isinstance(grid, GridLayoutPort))
    print("imgvar isinstance CopyRenderSpecPort     :", isinstance(imgvar, CopyRenderSpecPort))

    # 1) Create a grid (2x2 uniform, 100x100 canvas).
    grid_id = grid.create_grid_canvas("demo", grid_rows=2, grid_cols=2,
                                      canvas_size=PixelSize(100, 100)).unwrap()

    # 2) IMAGE_VARIANT creates a real ImageCopy with both crops set (R-08 test).
    asset_id = imgvar.import_image_asset(b"IMG:10x10:demo", "demo.png",
                                         "image/png").unwrap()
    copy_id = imgvar.create_image_copy(asset_id, copy_name="top left").unwrap()
    imgvar.change_auto_crop_settings(copy_id, target_color_argb=0xFFFFFFFF, threshold=10)
    imgvar.change_manual_crop_settings(copy_id, x=0.1, y=0.1, width=0.5, height=0.5)

    # 3) GRID UC-05 places that existing copy -> succeeds (Port returns True).
    place_res = grid.place_image_copy(grid_id, copy_id,
                                      CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(place_res, Ok)

    # 4) GRID UC-05 with an unknown copy -> UnknownCopyId (Port returns False).
    unknown = uuid.uuid4()
    unknown_res = grid.place_image_copy(grid_id, unknown,
                                        CellPosition(1, 1), OccupySize(1, 1))
    err = unknown_res.error
    assert isinstance(err, UnknownCopyId) and err.copy_id == unknown

    # 5) RENDERING builds the model by reading BOTH producers through the ports.
    model = render.build_render_model(grid_id).unwrap()
    print("\n--- RENDERING (consumer of GRID + IMGVAR) ---")
    print("render model items:", len(model.items))
    item = model.items[0]
    print("item pixel rect (px,py,pw,ph):", (item.px, item.py, item.pw, item.ph))
    # R-08 application: manual crop wins over auto crop.
    print("effective crop kind (R-08 manual>auto):", item.effective_crop.kind)
    assert item.effective_crop.kind == "manual", "R-08: manual must win over auto"
    descriptor = render.export_render_descriptor(grid_id).unwrap()
    print("descriptor item_count:", len(descriptor["items"]))

    print("\nADAPTER LINE COUNT AT BOUNDARY: 0")
    print("  GRID <-> IMGVAR  : 0")
    print("  RENDERING <-> GRID  : 0")
    print("  RENDERING <-> IMGVAR: 0")
    print("compose demo OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
