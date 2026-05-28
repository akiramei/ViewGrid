"""IMAGE_VARIANT Anchor tests AT-01..AT-10 (image-variant-management/30-design.md §8).

Note: these AT ids are namespaced by the IMAGE_VARIANT capability (function names
prefixed test_at_01_imgvar_* etc.) so they don't collide with GRID's AT ids while
remaining discoverable by `test_at_0N_` search.
"""

import random
import uuid

import image_variant_management.events as ev
from image_variant_management.failures import (
    DependentCopiesExist,
    InvalidAutoCropSettings,
    InvalidCopyName,
    InvalidManualCropFractions,
    NotFound,
)
from shared.result import Err, Ok

IMG = b"IMG:8x8:p"


def _asset(imgvar, data=IMG, name="a.png"):
    return imgvar.import_image_asset(data, name, "image/png").unwrap()


# AT-01 (W-1): hash dup returns existing + ImageAssetImportedAsDuplicate.
def test_at_01_imgvar_hash_dedup(imgvar, bus):
    a1 = _asset(imgvar)
    a2 = _asset(imgvar, IMG, "b.png")
    assert a1 == a2
    assert len(bus.of_type(ev.ImageAssetImportedAsDuplicate)) == 1


# AT-02 (W-3): dependent copies -> DependentCopiesExist.
def test_at_02_imgvar_cascade_refusal(imgvar):
    a = _asset(imgvar)
    imgvar.create_image_copy(a)
    res = imgvar.delete_image_asset(a)
    assert isinstance(res.error, DependentCopiesExist)


# AT-03 (W-4): AutoCrop partial null -> InvalidAutoCropSettings.
def test_at_03_imgvar_autocrop_partial(imgvar):
    a = _asset(imgvar)
    c = imgvar.create_image_copy(a).unwrap()
    res = imgvar.change_auto_crop_settings(c, 0xFFFFFFFF, None)
    assert isinstance(res.error, InvalidAutoCropSettings)


# AT-04 (W-2): AutoCrop + ManualCrop coexist (R-08 not applied here).
def test_at_04_imgvar_autocrop_manualcrop_coexist(imgvar):
    a = _asset(imgvar)
    c = imgvar.create_image_copy(a).unwrap()
    imgvar.change_auto_crop_settings(c, 0xFFFFFFFF, 8)
    imgvar.change_manual_crop_settings(c, 0.1, 0.1, 0.5, 0.5)
    copy = imgvar.get_image_copy(c).unwrap()
    # BOTH present: this Capability does NOT override one with the other.
    assert copy.auto_crop is not None
    assert copy.manual_crop is not None


# AT-05: ManualCrop x + width > 1 -> InvalidManualCropFractions.
def test_at_05_imgvar_manualcrop_overflow(imgvar):
    a = _asset(imgvar)
    c = imgvar.create_image_copy(a).unwrap()
    res = imgvar.change_manual_crop_settings(c, 0.6, 0.1, 0.5, 0.5)
    assert isinstance(res.error, InvalidManualCropFractions)


# AT-06: Rename to null succeeds (back to auto name).
def test_at_06_imgvar_rename_null_ok(imgvar):
    a = _asset(imgvar)
    c = imgvar.create_image_copy(a, copy_name="x").unwrap()
    res = imgvar.rename_image_copy(c, None)
    assert isinstance(res, Ok)
    assert imgvar.get_image_copy(c).unwrap().copy_name is None


# AT-07: Rename to "" -> InvalidCopyName.
def test_at_07_imgvar_rename_empty_rejected(imgvar):
    a = _asset(imgvar)
    c = imgvar.create_image_copy(a).unwrap()
    res = imgvar.rename_image_copy(c, "")
    assert isinstance(res.error, InvalidCopyName)


# AT-08: CreateImageCopy on missing asset -> NotFound(ImageAsset).
def test_at_08_imgvar_create_copy_missing_asset(imgvar):
    res = imgvar.create_image_copy(uuid.uuid4())
    assert isinstance(res.error, NotFound)
    assert res.error.entity_kind == "ImageAsset"


# AT-09: deleted copy_id -> ImageCopyExists false.
def test_at_09_imgvar_exists_after_delete(imgvar):
    a = _asset(imgvar)
    c = imgvar.create_image_copy(a).unwrap()
    imgvar.delete_image_copy(c)
    assert imgvar.image_copy_exists(c) is False


# AT-10: 1000-step random walk; R-02, R-03, R-06, R-07 always hold.
def test_at_10_imgvar_random_walk(imgvar):
    rng = random.Random(20260529)
    assets: list = []
    copies: list = []

    def check_invariants():
        # R-02: hash <-> asset 1:1 (no two assets share a hash).
        hashes = [imgvar._assets.get_by_id(a).file_hash for a in assets
                  if imgvar.image_asset_exists(a)]
        assert len(hashes) == len(set(hashes))
        # R-03: every copy references an existing asset.
        for c in list(copies):
            copy = imgvar._copies.get_by_id(c)
            if copy is not None:
                assert imgvar.image_asset_exists(copy.asset_id)
        # R-06 / R-07: aggregate consistency on all live copies.
        for copy in imgvar.list_image_copies():
            if copy.auto_crop is not None:
                assert 0 <= copy.auto_crop.threshold <= 255
            if copy.manual_crop is not None:
                m = copy.manual_crop
                assert m.x + m.width <= 1.0 + 1e-9
                assert m.y + m.height <= 1.0 + 1e-9

    for step in range(1000):
        op = rng.randint(0, 6)
        if op == 0:  # import (unique or dup)
            data = f"IMG:4x4:{rng.randint(0, 5)}".encode()
            res = imgvar.import_image_asset(data, "x.png", "image/png")
            if isinstance(res, Ok) and res.value not in assets:
                assets.append(res.value)
        elif op == 1 and assets:  # create copy
            a = rng.choice(assets)
            if imgvar.image_asset_exists(a):
                res = imgvar.create_image_copy(a)
                if isinstance(res, Ok):
                    copies.append(res.value)
        elif op == 2 and copies:  # delete copy
            c = rng.choice(copies)
            imgvar.delete_image_copy(c)
        elif op == 3 and assets:  # try delete asset (may refuse)
            a = rng.choice(assets)
            imgvar.delete_image_asset(a)
        elif op == 4 and copies:  # autocrop on/off
            c = rng.choice(copies)
            if rng.random() < 0.5:
                imgvar.change_auto_crop_settings(c, 0xFF00FF00, rng.randint(0, 255))
            else:
                imgvar.change_auto_crop_settings(c, None, None)
        elif op == 5 and copies:  # manualcrop on/off
            c = rng.choice(copies)
            if rng.random() < 0.5:
                imgvar.change_manual_crop_settings(c, 0.1, 0.1, 0.4, 0.4)
            else:
                imgvar.change_manual_crop_settings(c, None, None, None, None)
        elif op == 6 and copies:  # rename
            c = rng.choice(copies)
            imgvar.rename_image_copy(c, rng.choice([None, "n1", "n2"]))
        check_invariants()
