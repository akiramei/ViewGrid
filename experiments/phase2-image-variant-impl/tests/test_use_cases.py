"""UseCase happy-path + failure-path tests for UC-01..UC-17."""

from __future__ import annotations

from image_variant_management import (
    Alignment,
    DependentCopiesExist,
    EventBus,
    Failure,
    ImageTransform,
    ImageVariantManagementService,
    InvalidAlignment,
    InvalidOccupySize,
    InvalidScalingMode,
    InvalidTransform,
    NotFound,
    OccupySize,
    Ok,
    Rotation,
    ScalingMode,
)


# Helpers ---------------------------------------------------------------------


def _import(service: ImageVariantManagementService, blob: bytes = b"<png-100x80>"):
    r = service.import_image_asset(
        image_bytes=blob, original_filename="x.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    return r.value


def _create(service: ImageVariantManagementService, asset_id: str):
    r = service.create_image_copy(asset_id=asset_id)
    assert isinstance(r, Ok)
    return r.value


# UC-01 -----------------------------------------------------------------------


def test_uc01_imports_and_sizes(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    assert asset.size.width == 100 and asset.size.height == 80
    assert len(asset.file_hash) == 64
    assert asset.file_hash == asset.file_hash.lower()


# UC-03 / UC-04 --------------------------------------------------------------


def test_uc03_lists_assets(service: ImageVariantManagementService) -> None:
    _import(service, b"<png-100x80>")
    _import(service, b"<jpg-50x50>")
    r = service.list_image_assets()
    assert isinstance(r, Ok)
    assert len(r.value) == 2


def test_uc04_get_asset_not_found(service: ImageVariantManagementService) -> None:
    r = service.get_image_asset(asset_id="missing")
    assert isinstance(r, Failure)
    assert isinstance(r.reason, NotFound)


# UC-05 -----------------------------------------------------------------------


def test_uc05_create_copy_defaults(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    r = service.create_image_copy(asset_id=asset.id)
    assert isinstance(r, Ok)
    copy = r.value
    assert copy.asset_id == asset.id
    assert copy.transform == ImageTransform()
    assert copy.scaling_mode == ScalingMode.UNIFORM_CONTAIN
    assert copy.alignment == Alignment.MIDDLE_CENTER
    assert copy.default_occupy_size == OccupySize(width=1, height=1)
    assert copy.auto_crop is None
    assert copy.manual_crop is None
    assert copy.copy_name is None


def test_uc05_custom_initial_values(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    r = service.create_image_copy(
        asset_id=asset.id,
        copy_name="custom",
        initial_transform=ImageTransform(rotation=Rotation.CW90, flip_x=True, flip_y=False),
        initial_scaling_mode=ScalingMode.FILL,
        initial_alignment=Alignment.TOP_LEFT,
        initial_occupy_size=OccupySize(width=3, height=2),
    )
    assert isinstance(r, Ok)
    copy = r.value
    assert copy.copy_name == "custom"
    assert copy.transform.rotation == Rotation.CW90
    assert copy.scaling_mode == ScalingMode.FILL
    assert copy.alignment == Alignment.TOP_LEFT
    assert copy.default_occupy_size == OccupySize(width=3, height=2)


# UC-06 -----------------------------------------------------------------------


def test_uc06_delete_copy(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.delete_image_copy(copy_id=copy.id)
    assert isinstance(r, Ok)
    r2 = service.get_image_copy(copy_id=copy.id)
    assert isinstance(r2, Failure)


# UC-07 list filter -----------------------------------------------------------


def test_uc07_filter_by_asset(service: ImageVariantManagementService) -> None:
    a1 = _import(service, b"<png-100x80>")
    a2 = _import(service, b"<jpg-50x50>")
    c1 = _create(service, a1.id)
    c2 = _create(service, a1.id)
    c3 = _create(service, a2.id)
    all_copies = service.list_image_copies()
    assert isinstance(all_copies, Ok) and len(all_copies.value) == 3
    filtered = service.list_image_copies(asset_id=a1.id)
    assert isinstance(filtered, Ok)
    ids = {c.id for c in filtered.value}
    assert ids == {c1.id, c2.id}


# UC-09 -----------------------------------------------------------------------


def test_uc09_change_transform(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.change_copy_transform(
        copy_id=copy.id, new_transform=ImageTransform(rotation=Rotation.CW180, flip_x=True, flip_y=False)
    )
    assert isinstance(r, Ok)
    assert r.value.transform.rotation == Rotation.CW180


def test_uc09_invalid_transform_rejected(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.change_copy_transform(copy_id=copy.id, new_transform="not a transform")  # type: ignore[arg-type]
    assert isinstance(r, Failure) and isinstance(r.reason, InvalidTransform)


# UC-10 -----------------------------------------------------------------------


def test_uc10_change_scaling_mode(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.change_scaling_mode(copy_id=copy.id, new_scaling_mode=ScalingMode.FILL)
    assert isinstance(r, Ok)
    assert r.value.scaling_mode == ScalingMode.FILL


# UC-11 -----------------------------------------------------------------------


def test_uc11_change_alignment(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.change_alignment(copy_id=copy.id, new_alignment=Alignment.BOTTOM_RIGHT)
    assert isinstance(r, Ok)
    assert r.value.alignment == Alignment.BOTTOM_RIGHT


def test_uc11_invalid_alignment_str_rejected(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.change_alignment(copy_id=copy.id, new_alignment="BottomRight")  # type: ignore[arg-type]
    assert isinstance(r, Failure) and isinstance(r.reason, InvalidAlignment)


# UC-14 -----------------------------------------------------------------------


def test_uc14_change_occupy_size(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.change_default_occupy_size(copy_id=copy.id, new_occupy_size=OccupySize(width=4, height=3))
    assert isinstance(r, Ok)
    assert r.value.default_occupy_size == OccupySize(width=4, height=3)


# UC-16 -----------------------------------------------------------------------


def test_uc16_image_copy_exists(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    copy = _create(service, asset.id)
    r = service.image_copy_exists(copy_id=copy.id)
    assert isinstance(r, Ok) and r.value is True
    r2 = service.image_copy_exists(copy_id="missing")
    assert isinstance(r2, Ok) and r2.value is False


# UC-17 -----------------------------------------------------------------------


def test_uc17_image_asset_exists(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    r = service.image_asset_exists(asset_id=asset.id)
    assert isinstance(r, Ok) and r.value is True
    r2 = service.image_asset_exists(asset_id="missing")
    assert isinstance(r2, Ok) and r2.value is False


# UC-02 happy path ------------------------------------------------------------


def test_uc02_delete_asset_with_no_copies(service: ImageVariantManagementService) -> None:
    asset = _import(service)
    r = service.delete_image_asset(asset_id=asset.id)
    assert isinstance(r, Ok)
    r2 = service.get_image_asset(asset_id=asset.id)
    assert isinstance(r2, Failure)


def test_uc02_delete_asset_refused_when_copies_exist(
    service: ImageVariantManagementService,
) -> None:
    asset = _import(service)
    c1 = _create(service, asset.id)
    c2 = _create(service, asset.id)
    r = service.delete_image_asset(asset_id=asset.id)
    assert isinstance(r, Failure)
    assert isinstance(r.reason, DependentCopiesExist)
    assert set(r.reason.dependent_copy_ids) == {c1.id, c2.id}
    # Asset still exists.
    g = service.get_image_asset(asset_id=asset.id)
    assert isinstance(g, Ok)
