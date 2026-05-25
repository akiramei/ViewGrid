"""Rule unit tests — R-01..R-07, R-09..R-11.

R-08 is intentionally tested in test_anchors.py (AT-04, "coexistence only").
"""

from __future__ import annotations

import pytest

from image_variant_management import (
    Alignment,
    AutoCropSettings,
    Failure,
    ImageTransform,
    ImageVariantManagementService,
    InvalidAutoCropSettings,
    InvalidImageData,
    InvalidManualCropFractions,
    InvalidScalingMode,
    ManualCropFraction,
    OccupySize,
    Ok,
    PixelSize,
    Rotation,
    ScalingMode,
    UnsupportedMimeType,
)


# ----------------------------------------------------------------------------
# R-01 ImageAssetMustHaveValidImageData
# ----------------------------------------------------------------------------


class TestR01_ImageDataValid:
    def test_valid_image_decodes(self, service: ImageVariantManagementService) -> None:
        result = service.import_image_asset(
            image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
        )
        assert isinstance(result, Ok)

    def test_undecodable_bytes_rejected(
        self, service: ImageVariantManagementService
    ) -> None:
        result = service.import_image_asset(
            image_bytes=b"not a real image", original_filename="x.png", mime_type="image/png"
        )
        assert isinstance(result, Failure)
        assert isinstance(result.reason, InvalidImageData)


# ----------------------------------------------------------------------------
# R-02 ImageAssetFileHashMustBeUnique  (UseCase-layer, NOT DB constraint)
# ----------------------------------------------------------------------------


class TestR02_HashUnique:
    def test_duplicate_bytes_return_existing_asset(
        self, service: ImageVariantManagementService
    ) -> None:
        r1 = service.import_image_asset(
            image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
        )
        r2 = service.import_image_asset(
            image_bytes=b"<png-100x80>", original_filename="a-again.png", mime_type="image/png"
        )
        assert isinstance(r1, Ok) and isinstance(r2, Ok)
        assert r1.value.id == r2.value.id
        listed = service.list_image_assets()
        assert isinstance(listed, Ok) and len(listed.value) == 1

    def test_different_bytes_same_size_remain_distinct(
        self, service: ImageVariantManagementService
    ) -> None:
        r1 = service.import_image_asset(
            image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
        )
        r2 = service.import_image_asset(
            image_bytes=b"<png-100x80>-alt", original_filename="b.png", mime_type="image/png"
        )
        assert isinstance(r1, Ok) and isinstance(r2, Ok)
        assert r1.value.id != r2.value.id
        assert r1.value.file_hash != r2.value.file_hash


# ----------------------------------------------------------------------------
# R-03 ImageCopyMustReferenceExistingAsset
# ----------------------------------------------------------------------------


class TestR03_CopyReferencesAsset:
    def test_create_copy_without_asset_fails(
        self, service: ImageVariantManagementService
    ) -> None:
        result = service.create_image_copy(asset_id="nonexistent-id")
        assert isinstance(result, Failure)
        from image_variant_management import NotFound

        assert isinstance(result.reason, NotFound)
        assert result.reason.entity_kind == "ImageAsset"


# ----------------------------------------------------------------------------
# R-04 ScalingMode enum
# ----------------------------------------------------------------------------


class TestR04_ScalingModeEnum:
    def test_enum_values_match_spec(self) -> None:
        names = {m.value for m in ScalingMode}
        assert names == {"UniformContain", "UniformCover", "Fill"}

    def test_change_scaling_mode_with_non_enum_fails(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        bad = service.change_scaling_mode(copy_id=copy.id, new_scaling_mode="UniformContain")  # type: ignore[arg-type]
        assert isinstance(bad, Failure)
        assert isinstance(bad.reason, InvalidScalingMode)


# ----------------------------------------------------------------------------
# R-05 Alignment enum
# ----------------------------------------------------------------------------


class TestR05_AlignmentEnum:
    def test_enum_values_match_spec(self) -> None:
        names = {m.value for m in Alignment}
        assert names == {
            "TopLeft",
            "TopCenter",
            "TopRight",
            "MiddleLeft",
            "MiddleCenter",
            "MiddleRight",
            "BottomLeft",
            "BottomCenter",
            "BottomRight",
        }


# ----------------------------------------------------------------------------
# R-06 AutoCropSettings aggregate
# ----------------------------------------------------------------------------


class TestR06_AutoCropAggregate:
    def test_both_null_is_off(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_auto_crop_settings(
            copy_id=copy.id, target_color_argb=None, threshold=None
        )
        assert isinstance(r, Ok)
        assert r.value.auto_crop is None

    def test_both_set_is_valid(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_auto_crop_settings(
            copy_id=copy.id, target_color_argb=0xFFFFFFFF, threshold=8
        )
        assert isinstance(r, Ok)
        assert r.value.auto_crop is not None
        assert r.value.auto_crop.target_color_argb == 0xFFFFFFFF
        assert r.value.auto_crop.threshold == 8

    def test_only_target_color_set_rejected(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_auto_crop_settings(
            copy_id=copy.id, target_color_argb=0xFF000000, threshold=None
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, InvalidAutoCropSettings)

    def test_only_threshold_set_rejected(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_auto_crop_settings(
            copy_id=copy.id, target_color_argb=None, threshold=5
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, InvalidAutoCropSettings)

    def test_threshold_out_of_range_rejected(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_auto_crop_settings(
            copy_id=copy.id, target_color_argb=0x00, threshold=300
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, InvalidAutoCropSettings)

    def test_constructing_auto_crop_with_oob_target_color_raises(self) -> None:
        with pytest.raises(ValueError):
            AutoCropSettings(target_color_argb=0x1_0000_0000, threshold=10)


# ----------------------------------------------------------------------------
# R-07 ManualCropFraction range
# ----------------------------------------------------------------------------


class TestR07_ManualCropFraction:
    def test_all_null_is_off(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_manual_crop_settings(
            copy_id=copy.id, x=None, y=None, width=None, height=None
        )
        assert isinstance(r, Ok)
        assert r.value.manual_crop is None

    def test_valid_fraction_accepted(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_manual_crop_settings(
            copy_id=copy.id, x=0.1, y=0.2, width=0.5, height=0.5
        )
        assert isinstance(r, Ok)
        assert r.value.manual_crop is not None
        assert r.value.manual_crop.x == 0.1

    def test_partial_null_rejected(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_manual_crop_settings(
            copy_id=copy.id, x=0.1, y=0.2, width=None, height=0.5
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, InvalidManualCropFractions)

    def test_value_out_of_range_rejected(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_manual_crop_settings(
            copy_id=copy.id, x=-0.1, y=0.0, width=0.5, height=0.5
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, InvalidManualCropFractions)

    def test_x_plus_width_over_one_rejected(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.change_manual_crop_settings(
            copy_id=copy.id, x=0.6, y=0.0, width=0.5, height=0.5
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, InvalidManualCropFractions)


# ----------------------------------------------------------------------------
# R-09 Rotation enum
# ----------------------------------------------------------------------------


class TestR09_RotationEnum:
    def test_enum_values(self) -> None:
        names = {m.value for m in Rotation}
        assert names == {"None", "CW90", "CW180", "CW270"}

    def test_change_transform_with_non_enum_rotation_fails(
        self, service: ImageVariantManagementService
    ) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        with pytest.raises(ValueError):
            # Domain rejects construction
            ImageTransform(rotation="CW90")  # type: ignore[arg-type]


# ----------------------------------------------------------------------------
# R-10 OccupySize positive
# ----------------------------------------------------------------------------


class TestR10_OccupySizePositive:
    def test_zero_width_rejected(self) -> None:
        with pytest.raises(ValueError):
            OccupySize(width=0, height=1)

    def test_negative_height_rejected(self) -> None:
        with pytest.raises(ValueError):
            OccupySize(width=1, height=-3)

    def test_one_one_is_valid(self) -> None:
        size = OccupySize(width=1, height=1)
        assert size.width == 1 and size.height == 1


# ----------------------------------------------------------------------------
# R-11 ImageCopyName null-or-nonempty
# ----------------------------------------------------------------------------


class TestR11_CopyName:
    def test_none_is_valid(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.rename_image_copy(copy_id=copy.id, new_name=None)
        assert isinstance(r, Ok)

    def test_nonempty_is_valid(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.rename_image_copy(copy_id=copy.id, new_name="左半分")
        assert isinstance(r, Ok)
        assert r.value.copy_name == "左半分"

    def test_empty_string_rejected(self, service: ImageVariantManagementService) -> None:
        asset = _imported_asset(service)
        copy = _created_copy(service, asset.id)
        r = service.rename_image_copy(copy_id=copy.id, new_name="")
        assert isinstance(r, Failure)
        from image_variant_management import InvalidCopyName

        assert isinstance(r.reason, InvalidCopyName)


# ----------------------------------------------------------------------------
# Unsupported MIME type guard (UC-01)
# ----------------------------------------------------------------------------


class TestUnsupportedMime:
    def test_unsupported_mime_rejected(self, service: ImageVariantManagementService) -> None:
        r = service.import_image_asset(
            image_bytes=b"<png-100x80>", original_filename="a.tiff", mime_type="image/tiff"
        )
        assert isinstance(r, Failure)
        assert isinstance(r.reason, UnsupportedMimeType)


# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------


def _imported_asset(service: ImageVariantManagementService):
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    return r.value


def _created_copy(service: ImageVariantManagementService, asset_id: str):
    r = service.create_image_copy(asset_id=asset_id)
    assert isinstance(r, Ok)
    return r.value
