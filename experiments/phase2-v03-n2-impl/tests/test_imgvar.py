"""IMAGE_VARIANT_MANAGEMENT tests: rules, UC happy/failure, events, anchors AT-01..AT-10."""
from __future__ import annotations

import random
import uuid

import pytest

from image_variant_management import events as ev
from image_variant_management import failures as fail
from image_variant_management.domain import (
    AutoCropSettings,
    ImageTransform,
    ManualCropFraction,
)
from image_variant_management.enums import Alignment, Rotation, ScalingMode
from image_variant_management.use_cases import ImageVariantManagementUseCases
from shared.events import RecordingBus
from shared.result import Err, Ok
from shared.value_objects import OccupySize


def make_uc(bus=None):
    return ImageVariantManagementUseCases(bus=bus or RecordingBus())


def make_asset(uc, data=b"img-bytes", name="photo.png"):
    res = uc.import_image_asset(data, name, "image/png")
    assert isinstance(res, Ok)
    return res.value


def make_copy(uc, asset=None, **kw):
    asset = asset or make_asset(uc)
    res = uc.create_image_copy(asset.id, **kw)
    assert isinstance(res, Ok)
    return res.value


# ---------------------------------------------------------------- Rule units
def test_r06_autocrop_both_or_neither():
    with pytest.raises(ValueError):
        AutoCropSettings(target_color_argb=0xFFFFFFFF, threshold=300)
    AutoCropSettings(target_color_argb=0xFFFFFFFF, threshold=8)


def test_r07_manualcrop_normalized():
    with pytest.raises(ValueError):
        ManualCropFraction(0.6, 0.0, 0.6, 0.1)  # x+w>1
    ManualCropFraction(0.1, 0.1, 0.5, 0.5)


def test_r09_rotation_enum():
    t = ImageTransform(rotation=Rotation.CW90)
    assert t.rotation is Rotation.CW90
    assert Rotation.NoRotation.value == "None"


def test_r10_occupy_positive():
    with pytest.raises(ValueError):
        OccupySize(0, 1)


def test_r11_copy_name_null_or_nonempty():
    uc = make_uc()
    asset = make_asset(uc)
    res = uc.create_image_copy(asset.id, copy_name="")
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidCopyName)


# ---------------------------------------------------------------- UC happy
def test_uc01_import():
    uc = make_uc()
    res = uc.import_image_asset(b"abc", "x.png", "image/png")
    assert isinstance(res, Ok)


def test_uc05_create_copy_defaults():
    uc = make_uc()
    c = make_copy(uc)
    assert c.scaling_mode is ScalingMode.UniformContain
    assert c.alignment is Alignment.MiddleCenter
    assert c.transform.rotation is Rotation.NoRotation


def test_uc09_to_15_changes():
    uc = make_uc()
    c = make_copy(uc)
    assert isinstance(uc.change_copy_transform(c.id, ImageTransform(Rotation.CW180)), Ok)
    assert isinstance(uc.change_scaling_mode(c.id, ScalingMode.Fill), Ok)
    assert isinstance(uc.change_alignment(c.id, Alignment.TopLeft), Ok)
    assert isinstance(uc.change_auto_crop_settings(c.id, 0xFFFFFFFF, 8), Ok)
    assert isinstance(uc.change_manual_crop_settings(c.id, 0.1, 0.1, 0.5, 0.5), Ok)
    assert isinstance(uc.change_default_occupy_size(c.id, OccupySize(2, 2)), Ok)
    assert isinstance(uc.rename_image_copy(c.id, "renamed"), Ok)


def test_uc16_17_exists():
    uc = make_uc()
    asset = make_asset(uc)
    c = make_copy(uc, asset=asset)
    assert uc.image_copy_exists(c.id) is True
    assert uc.image_asset_exists(asset.id) is True
    assert uc.image_copy_exists(uuid.uuid4()) is False


# ---------------------------------------------------------------- UC failures
def test_uc01_unsupported_mime():
    uc = make_uc()
    res = uc.import_image_asset(b"abc", "x.gif", "image/gif")
    assert isinstance(res, Err) and isinstance(res.error, fail.UnsupportedMimeType)


def test_uc01_invalid_image_data():
    uc = make_uc()
    res = uc.import_image_asset(b"", "x.png", "image/png")
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidImageData)


def test_uc05_notfound_asset():
    uc = make_uc()
    res = uc.create_image_copy(uuid.uuid4())
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "ImageAsset"


def test_uc12_invalid_autocrop_one_null():
    uc = make_uc()
    c = make_copy(uc)
    res = uc.change_auto_crop_settings(c.id, 0xFFFFFFFF, None)
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidAutoCropSettings)


def test_uc13_invalid_manualcrop():
    uc = make_uc()
    c = make_copy(uc)
    res = uc.change_manual_crop_settings(c.id, 0.6, 0.0, 0.6, 0.1)  # x+w>1
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidManualCropFractions)


# ---------------------------------------------------------------- Events
def test_event_copy_created():
    bus = RecordingBus()
    uc = make_uc(bus=bus)
    asset = make_asset(uc)
    bus.clear()
    uc.create_image_copy(asset.id)
    assert len(bus.of_type(ev.ImageCopyCreated)) == 1


# ---------------------------------------------------------------- Anchor tests
def test_at_01_hash_dedup_emits_duplicate():
    bus = RecordingBus()
    uc = make_uc(bus=bus)
    uc.import_image_asset(b"same-bytes", "a.png", "image/png")
    bus.clear()
    res = uc.import_image_asset(b"same-bytes", "b.png", "image/png")
    assert isinstance(res, Ok)
    assert len(bus.of_type(ev.ImageAssetImportedAsDuplicate)) == 1
    assert len(bus.of_type(ev.ImageAssetImported)) == 0
    assert len(uc.list_image_assets()) == 1


def test_at_02_delete_asset_with_dependents():
    uc = make_uc()
    asset = make_asset(uc)
    c1 = make_copy(uc, asset=asset)
    c2 = make_copy(uc, asset=asset)
    res = uc.delete_image_asset(asset.id)
    assert isinstance(res, Err) and isinstance(res.error, fail.DependentCopiesExist)
    assert set(res.error.dependent_copy_ids) == {c1.id, c2.id}


def test_at_03_autocrop_one_null_rejected():
    uc = make_uc()
    c = make_copy(uc)
    res = uc.change_auto_crop_settings(c.id, 0xFFFFFFFF, None)
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidAutoCropSettings)


def test_at_04_autocrop_manualcrop_coexist():
    uc = make_uc()
    c = make_copy(uc)
    uc.change_auto_crop_settings(c.id, 0xFFFFFFFF, 8)
    res = uc.change_manual_crop_settings(c.id, 0.1, 0.1, 0.5, 0.5)
    assert isinstance(res, Ok)
    updated = uc.get_image_copy(c.id).value
    assert updated.auto_crop is not None and updated.manual_crop is not None


def test_at_05_manualcrop_overflow():
    uc = make_uc()
    c = make_copy(uc)
    res = uc.change_manual_crop_settings(c.id, 0.5, 0.0, 0.6, 0.1)
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidManualCropFractions)


def test_at_06_rename_to_null_ok():
    uc = make_uc()
    c = make_copy(uc, copy_name="foo")
    res = uc.rename_image_copy(c.id, None)
    assert isinstance(res, Ok) and res.value.copy_name is None


def test_at_07_rename_to_empty_rejected():
    uc = make_uc()
    c = make_copy(uc)
    res = uc.rename_image_copy(c.id, "")
    assert isinstance(res, Err) and isinstance(res.error, fail.InvalidCopyName)


def test_at_08_create_copy_unknown_asset():
    uc = make_uc()
    res = uc.create_image_copy(uuid.uuid4())
    assert isinstance(res, Err) and isinstance(res.error, fail.NotFound)
    assert res.error.entity_kind == "ImageAsset"


def test_at_09_exists_false_after_delete():
    uc = make_uc()
    c = make_copy(uc)
    uc.delete_image_copy(c.id)
    assert uc.image_copy_exists(c.id) is False


def test_at_10_random_walk_1000_steps():
    rng = random.Random(20260529)
    uc = make_uc()
    asset_ids: list[uuid.UUID] = []
    copy_ids: list[uuid.UUID] = []

    for i in range(1000):
        op = rng.choice(["import", "create", "delete_copy", "delete_asset", "autocrop", "manualcrop", "rename"])
        if op == "import":
            r = uc.import_image_asset(f"bytes-{rng.randint(0, 20)}".encode(), "x.png", "image/png")
            if isinstance(r, Ok) and r.value.id not in asset_ids:
                asset_ids.append(r.value.id)
        elif op == "create" and asset_ids:
            r = uc.create_image_copy(rng.choice(asset_ids))
            if isinstance(r, Ok):
                copy_ids.append(r.value.id)
        elif op == "delete_copy" and copy_ids:
            cid = rng.choice(copy_ids)
            if isinstance(uc.delete_image_copy(cid), Ok):
                copy_ids.remove(cid)
        elif op == "delete_asset" and asset_ids:
            aid = rng.choice(asset_ids)
            r = uc.delete_image_asset(aid)
            if isinstance(r, Ok):
                asset_ids.remove(aid)
        elif op == "autocrop" and copy_ids:
            uc.change_auto_crop_settings(rng.choice(copy_ids), 0xFF00FF00, rng.randint(0, 255))
        elif op == "manualcrop" and copy_ids:
            uc.change_manual_crop_settings(rng.choice(copy_ids), 0.0, 0.0, 0.5, 0.5)
        elif op == "rename" and copy_ids:
            uc.rename_image_copy(rng.choice(copy_ids), rng.choice([None, "n"]))

        # R-02: hash <-> asset 1:1 (no duplicate hashes).
        hashes = [a.file_hash for a in uc.list_image_assets()]
        assert len(hashes) == len(set(hashes))
        # R-03: every copy references an existing asset.
        live_assets = {a.id for a in uc.list_image_assets()}
        for cpy in uc.list_image_copies():
            assert cpy.asset_id is not None
            # asset may be intentionally kept (delete refused if dependents) -> must still exist
            assert cpy.asset_id in live_assets
        # R-06 / R-07: aggregates valid by construction (would have raised).
