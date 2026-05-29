"""compose.py (Phase A, n=2): wire GRID_COMPOSITION + IMAGE_VARIANT_MANAGEMENT.

Demonstrates the existing n=2 boundary with ZERO adapters:
  GRID UC-05 calls ImageCopyExistencePort.exists; IMGVAR satisfies the Port
  natively (its `exists` method). Same shared Protocol on both sides.

The v0.3 read ports (get_grid_layout / get_copy_render_spec) are already
present on the producers (pre-loaded) even though no consumer exists yet.
"""
from __future__ import annotations

import os
import sys
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from grid_composition.domain import CellPosition  # noqa: E402
from grid_composition.use_cases import GridCompositionUseCases  # noqa: E402
from image_variant_management.use_cases import ImageVariantManagementUseCases  # noqa: E402
from shared.events import RecordingBus  # noqa: E402
from shared.result import Ok  # noqa: E402
from shared.value_objects import OccupySize, PixelSize  # noqa: E402


def main() -> None:
    bus = RecordingBus()
    imgvar = ImageVariantManagementUseCases(bus=bus)
    # IMGVAR satisfies ImageCopyExistencePort natively -> no adapter.
    grid = GridCompositionUseCases(image_copy_existence=imgvar, bus=bus)

    # Create an asset + copy in IMGVAR.
    asset_res = imgvar.import_image_asset(b"hello-image-bytes", "photo.png", "image/png")
    assert isinstance(asset_res, Ok)
    copy_res = imgvar.create_image_copy(asset_res.value.id, copy_name="full")
    assert isinstance(copy_res, Ok)
    copy_id = copy_res.value.id

    # Create a grid in GRID.
    grid_res = grid.create_grid_canvas("demo", 3, 3, PixelSize(300, 300))
    assert isinstance(grid_res, Ok)
    grid_id = grid_res.value.id

    # Existing copy places OK (boundary call with no conversion).
    placed = grid.place_image_copy(grid_id, copy_id, CellPosition(0, 0), OccupySize(1, 1))
    assert isinstance(placed, Ok), placed
    print(f"[n=2] placed existing copy: order={placed.value.placement_order}")

    # Unknown copy -> UnknownCopyId.
    unknown = grid.place_image_copy(grid_id, uuid.uuid4(), CellPosition(1, 1), OccupySize(1, 1))
    assert type(unknown.error).__name__ == "UnknownCopyId", unknown
    print("[n=2] unknown copy correctly rejected with UnknownCopyId")

    # Pre-loaded read ports already work (no consumer yet, but they exist).
    layout = grid.get_grid_layout(grid_id)
    spec = imgvar.get_copy_render_spec(copy_id)
    print(f"[n=2] pre-loaded get_grid_layout -> {len(layout.placements)} placement(s)")
    print(f"[n=2] pre-loaded get_copy_render_spec -> scaling_mode={spec.scaling_mode}")
    print("compose (n=2) OK")


if __name__ == "__main__":
    main()
