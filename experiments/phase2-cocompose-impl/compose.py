"""Compose demo: wire GRID_COMPOSITION <-> IMAGE_VARIANT_MANAGEMENT.

Run:  python experiments/phase2-cocompose-impl/compose.py

This script proves the contract claim: ImageVariantManagementUseCases is passed
DIRECTLY into GridCompositionUseCases as the ImageCopyExistencePort. There is NO
adapter class anywhere -- ImageVariantManagementUseCases.exists(uuid) -> bool is
the Port, satisfied natively (it runs UC-16 internally).
"""

from __future__ import annotations

import os
import sys

# Allow `python experiments/phase2-cocompose-impl/compose.py` from any cwd.
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
from shared.eventbus import RecordingBus  # noqa: E402
from shared.ports import ImageCopyExistencePort  # noqa: E402
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
    return bus, imgvar, grid


def main() -> int:
    import uuid

    bus, imgvar, grid = build()

    # Runtime proof that imgvar satisfies the Port without any wrapper.
    assert isinstance(imgvar, ImageCopyExistencePort), "imgvar must satisfy the Port"
    print("imgvar isinstance ImageCopyExistencePort:", isinstance(imgvar, ImageCopyExistencePort))

    # 1) Create a grid.
    grid_res = grid.create_grid_canvas("demo", grid_rows=3, grid_cols=3,
                                       canvas_size=PixelSize(900, 900))
    grid_id = grid_res.unwrap()

    # 2) IMAGE_VARIANT creates a real ImageCopy.
    asset_id = imgvar.import_image_asset(b"IMG:10x10:demo", "demo.png",
                                         "image/png").unwrap()
    copy_id = imgvar.create_image_copy(asset_id, copy_name="left half").unwrap()

    # 3) GRID UC-05 places that existing copy -> succeeds (Port returns True).
    place_res = grid.place_image_copy(grid_id, copy_id,
                                      CellPosition(0, 0), OccupySize(1, 1))
    print("place existing copy ->", "Ok" if isinstance(place_res, Ok) else place_res)
    assert isinstance(place_res, Ok)

    # 4) GRID UC-05 with an unknown copy -> UnknownCopyId (Port returns False).
    unknown = uuid.uuid4()
    unknown_res = grid.place_image_copy(grid_id, unknown,
                                        CellPosition(1, 1), OccupySize(1, 1))
    err = unknown_res.error
    print("place unknown copy ->", type(err).__name__, getattr(err, "copy_id", None))
    assert isinstance(err, UnknownCopyId) and err.copy_id == unknown

    print("\nADAPTER LINE COUNT AT BOUNDARY: 0")
    print("compose demo OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
