"""IMAGE_VARIANT UseCase happy/failure/event/dedup/cascade tests (UC-01..UC-17)."""

import uuid

import image_variant_management.events as ev
from image_variant_management.domain import (
    Alignment,
    ImageTransform,
    Rotation,
    ScalingMode,
)
from image_variant_management.failures import (
    DependentCopiesExist,
    InvalidAutoCropSettings,
    InvalidImageData,
    InvalidManualCropFractions,
    NotFound,
    UnsupportedMimeType,
)
from shared.result import Err, Ok
from shared.value_objects import OccupySize

IMG = b"IMG:8x8:payload"


def _asset(imgvar, data=IMG, mime="image/png", name="a.png"):
    return imgvar.import_image_asset(data, name, mime).unwrap()


def _copy(imgvar, asset_id=None, name=None):
    if asset_id is None:
        asset_id = _asset(imgvar)
    return imgvar.create_image_copy(asset_id, copy_name=name).unwrap()


# --- UC-01 -------------------------------------------------------------
def test_uc01_happy(imgvar, bus):
    res = imgvar.import_image_asset(IMG, "a.png", "image/png")
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageAssetImported)) == 1


def test_uc01_unsupported_mime(imgvar):
    res = imgvar.import_image_asset(IMG, "a.bmp", "image/bmp")
    assert isinstance(res.error, UnsupportedMimeType)


def test_uc01_invalid_image_data(imgvar):
    res = imgvar.import_image_asset(b"not an image", "a.png", "image/png")
    assert isinstance(res.error, InvalidImageData)


def test_uc01_hash_dedup(imgvar, bus):
    a1 = imgvar.import_image_asset(IMG, "a.png", "image/png").unwrap()
    a2 = imgvar.import_image_asset(IMG, "b.png", "image/png").unwrap()
    assert a1 == a2  # same asset returned
    assert len(imgvar.list_image_assets()) == 1
    assert len(bus.of_type(ev.ImageAssetImportedAsDuplicate)) == 1
    assert len(bus.of_type(ev.ImageAssetImported)) == 1  # only the first one


# --- UC-02 -------------------------------------------------------------
def test_uc02_delete_no_dependents(imgvar, bus):
    a = _asset(imgvar)
    res = imgvar.delete_image_asset(a)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageAssetDeleted)) == 1


def test_uc02_dependent_copies_exist(imgvar):
    a = _asset(imgvar)
    c1 = imgvar.create_image_copy(a).unwrap()
    c2 = imgvar.create_image_copy(a).unwrap()
    res = imgvar.delete_image_asset(a)
    assert isinstance(res.error, DependentCopiesExist)
    assert set(res.error.dependent_copy_ids) == {c1, c2}
    # asset NOT deleted
    assert imgvar.image_asset_exists(a)


def test_uc02_not_found(imgvar):
    res = imgvar.delete_image_asset(uuid.uuid4())
    assert isinstance(res.error, NotFound)


# --- UC-05 -------------------------------------------------------------
def test_uc05_happy(imgvar, bus):
    a = _asset(imgvar)
    res = imgvar.create_image_copy(a, copy_name="left")
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyCreated)) == 1


def test_uc05_asset_not_found(imgvar):
    res = imgvar.create_image_copy(uuid.uuid4())
    assert isinstance(res.error, NotFound)
    assert res.error.entity_kind == "ImageAsset"


# --- UC-06 -------------------------------------------------------------
def test_uc06_delete_happy(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.delete_image_copy(c)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyDeleted)) == 1
    assert not imgvar.image_copy_exists(c)


def test_uc06_not_found(imgvar):
    res = imgvar.delete_image_copy(uuid.uuid4())
    assert isinstance(res.error, NotFound)


# --- UC-07 / UC-08 -----------------------------------------------------
def test_uc07_list_filter_by_asset(imgvar):
    a1 = _asset(imgvar, b"IMG:1x1:a", name="a.png")
    a2 = _asset(imgvar, b"IMG:1x1:b", name="b.png")
    imgvar.create_image_copy(a1)
    imgvar.create_image_copy(a2)
    assert len(imgvar.list_image_copies()) == 2
    assert len(imgvar.list_image_copies(a1)) == 1


def test_uc08_get_copy(imgvar):
    c = _copy(imgvar)
    assert isinstance(imgvar.get_image_copy(c), Ok)
    assert isinstance(imgvar.get_image_copy(uuid.uuid4()).error, NotFound)


# --- UC-09/10/11 (setting changes) -------------------------------------
def test_uc09_transform(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.change_copy_transform(c, ImageTransform(rotation=Rotation.CW90))
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyTransformChanged)) == 1


def test_uc10_scaling(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.change_scaling_mode(c, ScalingMode.Fill)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyScalingModeChanged)) == 1


def test_uc11_alignment(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.change_alignment(c, Alignment.TopLeft)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyAlignmentChanged)) == 1


# --- UC-12 -------------------------------------------------------------
def test_uc12_autocrop_on(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.change_auto_crop_settings(c, 0xFFFFFFFF, 8)
    assert isinstance(res, Ok)
    assert imgvar.get_image_copy(c).unwrap().auto_crop is not None
    assert len(bus.of_type(ev.ImageCopyAutoCropChanged)) == 1


def test_uc12_autocrop_off_both_null(imgvar):
    c = _copy(imgvar)
    imgvar.change_auto_crop_settings(c, 0xFFFFFFFF, 8)
    res = imgvar.change_auto_crop_settings(c, None, None)
    assert isinstance(res, Ok)
    assert imgvar.get_image_copy(c).unwrap().auto_crop is None


def test_uc12_autocrop_one_null_rejected(imgvar):
    c = _copy(imgvar)
    res = imgvar.change_auto_crop_settings(c, 0xFFFFFFFF, None)
    assert isinstance(res.error, InvalidAutoCropSettings)


# --- UC-13 -------------------------------------------------------------
def test_uc13_manualcrop_on(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.change_manual_crop_settings(c, 0.1, 0.1, 0.5, 0.5)
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyManualCropChanged)) == 1


def test_uc13_manualcrop_partial_null_rejected(imgvar):
    c = _copy(imgvar)
    res = imgvar.change_manual_crop_settings(c, 0.1, None, 0.5, 0.5)
    assert isinstance(res.error, InvalidManualCropFractions)


# --- UC-14 -------------------------------------------------------------
def test_uc14_default_occupy(imgvar, bus):
    c = _copy(imgvar)
    res = imgvar.change_default_occupy_size(c, OccupySize(2, 3))
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageCopyDefaultOccupySizeChanged)) == 1


# --- UC-15 -------------------------------------------------------------
def test_uc15_rename(imgvar, bus):
    c = _copy(imgvar, name="old")
    res = imgvar.rename_image_copy(c, "new")
    assert isinstance(res, Ok)
    assert imgvar.get_image_copy(c).unwrap().copy_name == "new"
    assert len(bus.of_type(ev.ImageCopyRenamed)) == 1


# --- UC-16 / UC-17 -----------------------------------------------------
def test_uc16_exists(imgvar):
    c = _copy(imgvar)
    assert imgvar.image_copy_exists(c)
    assert not imgvar.image_copy_exists(uuid.uuid4())


def test_uc17_asset_exists(imgvar):
    a = _asset(imgvar)
    assert imgvar.image_asset_exists(a)
    assert not imgvar.image_asset_exists(uuid.uuid4())
