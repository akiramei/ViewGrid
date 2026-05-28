"""pytest setup: put src/ on sys.path and provide shared fixtures.

This makes `python -m pytest experiments/phase2-cocompose-impl/ -q` work from
the repo root without any PYTHONPATH gymnastics (README documents the manual
PYTHONPATH alternative).
"""

from __future__ import annotations

import os
import sys

import pytest

_SRC = os.path.join(os.path.dirname(__file__), "..", "src")
sys.path.insert(0, os.path.abspath(_SRC))

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


@pytest.fixture
def bus() -> RecordingBus:
    return RecordingBus()


@pytest.fixture
def imgvar(bus: RecordingBus) -> ImageVariantManagementUseCases:
    return ImageVariantManagementUseCases(
        asset_repo=InMemoryImageAssetRepository(),
        copy_repo=InMemoryImageCopyRepository(),
        blob_storage=InMemoryBlobStorage(),
        bus=bus,
    )


@pytest.fixture
def grid(bus: RecordingBus, imgvar: ImageVariantManagementUseCases) -> GridCompositionUseCases:
    # imgvar injected DIRECTLY as the ImageCopyExistencePort (no adapter).
    return GridCompositionUseCases(
        grid_repo=InMemoryGridCanvasRepository(),
        placement_repo=InMemoryPlacementRepository(),
        image_copy_existence=imgvar,
        bus=bus,
    )


class AlwaysExistsPort:
    """Test double for GRID-only tests that don't care about IMAGE_VARIANT.

    NOTE: this is a *test* stand-in for the Port, NOT a boundary adapter. It is
    used only to exercise GRID UseCases in isolation; the compose tests use the
    real ImageVariantManagementUseCases with zero adapter code.
    """

    def exists(self, copy_id) -> bool:  # noqa: ANN001
        return True


@pytest.fixture
def grid_always(bus: RecordingBus) -> GridCompositionUseCases:
    return GridCompositionUseCases(
        grid_repo=InMemoryGridCanvasRepository(),
        placement_repo=InMemoryPlacementRepository(),
        image_copy_existence=AlwaysExistsPort(),
        bus=bus,
    )


@pytest.fixture
def render(
    bus: RecordingBus,
    grid: GridCompositionUseCases,
    imgvar: ImageVariantManagementUseCases,
) -> RenderingExportUseCases:
    # n=3 read boundary: grid IS the GridLayoutPort, imgvar IS the
    # CopyRenderSpecPort. Injected DIRECTLY, with ZERO adapter code.
    return RenderingExportUseCases(
        grid_layout=grid,
        copy_render_spec=imgvar,
        bus=bus,
    )
