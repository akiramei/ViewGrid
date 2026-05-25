"""Event emission tests — emission is tested independently of state mutation
where possible (per 30-design.md §4 and the audit_requirement that emission
be independently testable).
"""

from __future__ import annotations

from image_variant_management import (
    Alignment,
    EventBus,
    ImageAssetDeleted,
    ImageAssetImported,
    ImageAssetImportedAsDuplicate,
    ImageCopyAlignmentChanged,
    ImageCopyAutoCropChanged,
    ImageCopyCreated,
    ImageCopyDefaultOccupySizeChanged,
    ImageCopyDeleted,
    ImageCopyManualCropChanged,
    ImageCopyRenamed,
    ImageCopyScalingModeChanged,
    ImageCopyTransformChanged,
    ImageTransform,
    ImageVariantManagementService,
    OccupySize,
    Ok,
    Rotation,
    ScalingMode,
)


def _types(events: list[object]) -> list[type]:
    return [type(e) for e in events]


# UC-01 events ----------------------------------------------------------------


def test_import_emits_imported_for_new(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    assert _types(event_bus.recorded) == [ImageAssetImported]


def test_import_emits_only_duplicate_for_hash_dup(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    event_bus.clear()
    service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a-again.png", mime_type="image/png"
    )
    types = _types(event_bus.recorded)
    assert types == [ImageAssetImportedAsDuplicate]
    # In particular, ImageAssetImported must NOT be emitted on dup.
    assert ImageAssetImported not in types


# UC-02 events ----------------------------------------------------------------


def test_delete_asset_emits_deleted(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r1 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r1, Ok)
    event_bus.clear()
    r = service.delete_image_asset(asset_id=r1.value.id)
    assert isinstance(r, Ok)
    assert _types(event_bus.recorded) == [ImageAssetDeleted]


def test_delete_asset_refused_emits_nothing(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r1 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r1, Ok)
    service.create_image_copy(asset_id=r1.value.id)
    event_bus.clear()
    service.delete_image_asset(asset_id=r1.value.id)
    assert event_bus.recorded == []


# UC-05 / UC-06 ---------------------------------------------------------------


def test_create_and_delete_copy_emit_events(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r1 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r1, Ok)
    event_bus.clear()
    r2 = service.create_image_copy(asset_id=r1.value.id)
    assert isinstance(r2, Ok)
    assert _types(event_bus.recorded) == [ImageCopyCreated]
    event_bus.clear()
    service.delete_image_copy(copy_id=r2.value.id)
    assert _types(event_bus.recorded) == [ImageCopyDeleted]


# Setter events ---------------------------------------------------------------


def test_setter_events_emit_only_on_change(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r1 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r1, Ok)
    r2 = service.create_image_copy(asset_id=r1.value.id)
    assert isinstance(r2, Ok)
    copy_id = r2.value.id
    event_bus.clear()

    # Change each property; each emits exactly one corresponding event.
    service.change_copy_transform(
        copy_id=copy_id, new_transform=ImageTransform(rotation=Rotation.CW90)
    )
    service.change_scaling_mode(copy_id=copy_id, new_scaling_mode=ScalingMode.FILL)
    service.change_alignment(copy_id=copy_id, new_alignment=Alignment.TOP_LEFT)
    service.change_auto_crop_settings(
        copy_id=copy_id, target_color_argb=0xFFFFFFFF, threshold=8
    )
    service.change_manual_crop_settings(
        copy_id=copy_id, x=0.0, y=0.0, width=0.5, height=0.5
    )
    service.change_default_occupy_size(
        copy_id=copy_id, new_occupy_size=OccupySize(width=2, height=2)
    )
    service.rename_image_copy(copy_id=copy_id, new_name="X")

    assert _types(event_bus.recorded) == [
        ImageCopyTransformChanged,
        ImageCopyScalingModeChanged,
        ImageCopyAlignmentChanged,
        ImageCopyAutoCropChanged,
        ImageCopyManualCropChanged,
        ImageCopyDefaultOccupySizeChanged,
        ImageCopyRenamed,
    ]


def test_setter_no_op_emits_no_event(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r1 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r1, Ok)
    r2 = service.create_image_copy(asset_id=r1.value.id)
    assert isinstance(r2, Ok)
    event_bus.clear()
    # Setting the same value should not emit.
    service.change_scaling_mode(copy_id=r2.value.id, new_scaling_mode=ScalingMode.UNIFORM_CONTAIN)
    assert event_bus.recorded == []
