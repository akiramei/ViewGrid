"""IMAGE_VARIANT Rule unit tests (R-01..R-07, R-09..R-11; R-08 coexistence only)."""

import uuid

import pytest

from image_variant_management.domain import (
    Alignment,
    AutoCropSettings,
    ImageCopy,
    ImageTransform,
    ManualCropFraction,
    Rotation,
    ScalingMode,
)
from shared.value_objects import OccupySize


# --- R-06: AutoCropSettings both-or-neither ----------------------------
def test_r06_autocrop_valid():
    s = AutoCropSettings(0xFFFFFFFF, 8)
    assert s.threshold == 8


def test_r06_autocrop_threshold_range():
    with pytest.raises(ValueError):
        AutoCropSettings(0xFFFFFFFF, 300)


# --- R-07: ManualCropFraction normalized -------------------------------
def test_r07_manualcrop_valid():
    m = ManualCropFraction(0.1, 0.1, 0.5, 0.5)
    assert m.width == 0.5


def test_r07_manualcrop_out_of_range():
    with pytest.raises(ValueError):
        ManualCropFraction(0.1, 0.1, 1.5, 0.5)


def test_r07_manualcrop_x_plus_width_exceeds_one():
    with pytest.raises(ValueError):
        ManualCropFraction(0.6, 0.1, 0.5, 0.5)


# --- R-09: Rotation enumerated -----------------------------------------
def test_r09_rotation_enum():
    t = ImageTransform(rotation=Rotation.CW90)
    assert t.rotation is Rotation.CW90
    with pytest.raises(TypeError):
        ImageTransform(rotation="90deg")  # not an enum


# --- R-10: OccupySize positive (delegated to shared VO) ----------------
def test_r10_occupy_size_positive():
    with pytest.raises(ValueError):
        OccupySize(0, 1)


# --- R-11: CopyName null or non-empty ----------------------------------
def _make_copy(copy_name):
    import datetime as dt
    now = dt.datetime.now(dt.timezone.utc)
    return ImageCopy(
        id=uuid.uuid4(), asset_id=uuid.uuid4(), copy_name=copy_name,
        transform=ImageTransform(), scaling_mode=ScalingMode.UniformContain,
        alignment=Alignment.MiddleCenter, default_occupy_size=OccupySize(1, 1),
        auto_crop=None, manual_crop=None, created_at=now, updated_at=now)


def test_r11_null_name_ok():
    assert _make_copy(None).copy_name is None


def test_r11_empty_name_rejected():
    with pytest.raises(ValueError):
        _make_copy("")


def test_r11_nonempty_name_ok():
    assert _make_copy("left half").copy_name == "left half"


# --- R-04 / R-05: enum membership at construction -----------------------
def test_r04_r05_enum_membership():
    with pytest.raises(TypeError):
        _make_copy_with(scaling_mode="bad")
    with pytest.raises(TypeError):
        _make_copy_with(alignment="bad")


def _make_copy_with(scaling_mode=ScalingMode.UniformContain,
                    alignment=Alignment.MiddleCenter):
    import datetime as dt
    now = dt.datetime.now(dt.timezone.utc)
    return ImageCopy(
        id=uuid.uuid4(), asset_id=uuid.uuid4(), copy_name=None,
        transform=ImageTransform(), scaling_mode=scaling_mode,
        alignment=alignment, default_occupy_size=OccupySize(1, 1),
        auto_crop=None, manual_crop=None, created_at=now, updated_at=now)
