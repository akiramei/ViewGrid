"""Cross-Capability boundary tests.

These confirm the contracts described in 20-capability-bom.md §8 from the
IMAGE_VARIANT_MANAGEMENT side only.
"""

from __future__ import annotations

import io

import pytest

from image_variant_management import (
    EventBus,
    ImageCopyDeleted,
    ImageVariantManagementService,
    InMemoryImageAssetRepository,
    InMemoryImageBlobStorage,
    InMemoryImageCopyRepository,
    Ok,
    PillowImageDecoder,
)
from image_variant_management.adjacent_stubs import (
    GridCompositionStub,
    HistoryManagementStub,
    RenderingExportStub,
    WorkspaceManagementStub,
)


# Stub round-trip: GRID_COMPOSITION sees the existence query + deletion event.


def test_grid_composition_stub_sees_uc16_and_deletion(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    grid = GridCompositionStub(
        image_copy_exists=lambda cid: service.image_copy_exists(copy_id=cid).value,  # type: ignore[union-attr]
        event_bus=event_bus,
    )

    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)
    copy_id = c.value.id

    assert grid.check_image_copy_exists(copy_id) is True
    assert grid.check_image_copy_exists("missing") is False

    service.delete_image_copy(copy_id=copy_id)
    assert grid.check_image_copy_exists(copy_id) is False
    assert grid.observed_deleted_copy_ids == [copy_id]


def test_history_stub_records_every_event(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    history = HistoryManagementStub(event_bus=event_bus)
    service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert len(history.log) >= 1


# RENDERING_EXPORT stub: R-08 application lives there, NOT in this Capability.
# We just verify the stub provides the lookup; it does NOT touch UseCases.


def test_rendering_export_stub_picks_manual_when_both_set(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)
    service.change_auto_crop_settings(
        copy_id=c.value.id, target_color_argb=0xFF0000FF, threshold=4
    )
    after = service.change_manual_crop_settings(
        copy_id=c.value.id, x=0.0, y=0.0, width=0.5, height=0.5
    )
    assert isinstance(after, Ok)

    # R-08 is applied by RENDERING_EXPORT (the stub), NOT by this Capability.
    kind, value = RenderingExportStub.select_effective_crop(after.value)
    assert kind == "manual"  # Manual override (R-08 application — RENDERING_EXPORT's job)
    # ... but the stored ImageCopy retains BOTH.
    assert after.value.auto_crop is not None
    assert after.value.manual_crop is not None


# Smoke test for the real Pillow decoder.


def test_pillow_decoder_handles_real_png() -> None:
    pil = pytest.importorskip("PIL.Image")
    buffer = io.BytesIO()
    img = pil.new("RGB", (32, 16), color=(255, 0, 0))
    img.save(buffer, format="PNG")
    blob = buffer.getvalue()

    decoder = PillowImageDecoder()
    size = decoder.decode_size(blob, "image/png")
    assert size is not None
    assert size.width == 32 and size.height == 16

    # Garbage bytes -> None (mapped to InvalidImageData by the UseCase).
    assert decoder.decode_size(b"\x00garbage\x00", "image/png") is None


# Workspace stub: docs that a workspace simply hands repositories to us.


def test_workspace_stub_provides_repos() -> None:
    ws = WorkspaceManagementStub()
    assert isinstance(ws.asset_repo, InMemoryImageAssetRepository)
    assert isinstance(ws.copy_repo, InMemoryImageCopyRepository)
    assert isinstance(ws.blob_storage, InMemoryImageBlobStorage)
