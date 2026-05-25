"""Anchor Tests AT-01..AT-10 (30-design.md §8).

Expectations are FORBIDDEN to change. All 10 anchors must pass.
"""

from __future__ import annotations

import random

from image_variant_management import (
    AutoCropSettings,
    DependentCopiesExist,
    EventBus,
    Failure,
    ImageAssetImported,
    ImageAssetImportedAsDuplicate,
    ImageCopyDeleted,
    ImageTransform,
    ImageVariantManagementService,
    InvalidAutoCropSettings,
    InvalidCopyName,
    InvalidManualCropFractions,
    ManualCropFraction,
    NotFound,
    Ok,
    Rotation,
)


# ----------------------------------------------------------------------------
# AT-01 — Hash-duplicate import returns existing asset; emits
#         ImageAssetImportedAsDuplicate exactly once. ImageAssetImported only
#         fires the first time.
# ----------------------------------------------------------------------------


def test_at_01_hash_duplicate_returns_existing(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r1 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r1, Ok)
    event_bus.clear()

    r2 = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="b.png", mime_type="image/png"
    )
    assert isinstance(r2, Ok)
    assert r2.value.id == r1.value.id  # same asset

    dup_events = [e for e in event_bus.recorded if isinstance(e, ImageAssetImportedAsDuplicate)]
    new_events = [e for e in event_bus.recorded if isinstance(e, ImageAssetImported)]
    assert len(dup_events) == 1
    assert len(new_events) == 0


# ----------------------------------------------------------------------------
# AT-02 — Deleting an Asset with dependent copies returns DependentCopiesExist.
# ----------------------------------------------------------------------------


def test_at_02_dependent_copies_block_delete(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c1 = service.create_image_copy(asset_id=r.value.id)
    c2 = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c1, Ok) and isinstance(c2, Ok)

    d = service.delete_image_asset(asset_id=r.value.id)
    assert isinstance(d, Failure)
    assert isinstance(d.reason, DependentCopiesExist)
    assert d.reason.asset_id == r.value.id
    assert set(d.reason.dependent_copy_ids) == {c1.value.id, c2.value.id}


# ----------------------------------------------------------------------------
# AT-03 — AutoCropSettings with one side null is rejected as
#         InvalidAutoCropSettings (R-06).
# ----------------------------------------------------------------------------


def test_at_03_auto_crop_partial_null_rejected(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)

    bad = service.change_auto_crop_settings(
        copy_id=c.value.id, target_color_argb=0xFFFFFFFF, threshold=None
    )
    assert isinstance(bad, Failure)
    assert isinstance(bad.reason, InvalidAutoCropSettings)


# ----------------------------------------------------------------------------
# AT-04 — AutoCrop AND ManualCrop COEXIST on a single ImageCopy.
#         The override interpretation (R-08) is NOT applied here.
# ----------------------------------------------------------------------------


def test_at_04_auto_and_manual_crop_coexist(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)
    copy_id = c.value.id

    # 1) Set AutoCrop.
    after_auto = service.change_auto_crop_settings(
        copy_id=copy_id, target_color_argb=0xFFFFFFFF, threshold=8
    )
    assert isinstance(after_auto, Ok)
    assert after_auto.value.auto_crop == AutoCropSettings(target_color_argb=0xFFFFFFFF, threshold=8)
    assert after_auto.value.manual_crop is None

    # 2) Now set ManualCrop. AutoCrop must remain.
    after_manual = service.change_manual_crop_settings(
        copy_id=copy_id, x=0.1, y=0.1, width=0.5, height=0.5
    )
    assert isinstance(after_manual, Ok)
    final = after_manual.value
    assert final.auto_crop == AutoCropSettings(target_color_argb=0xFFFFFFFF, threshold=8)
    assert final.manual_crop == ManualCropFraction(x=0.1, y=0.1, width=0.5, height=0.5)

    # 3) Turning ManualCrop OFF should leave AutoCrop intact.
    off = service.change_manual_crop_settings(
        copy_id=copy_id, x=None, y=None, width=None, height=None
    )
    assert isinstance(off, Ok)
    assert off.value.auto_crop == AutoCropSettings(target_color_argb=0xFFFFFFFF, threshold=8)
    assert off.value.manual_crop is None


# ----------------------------------------------------------------------------
# AT-05 — ManualCrop with x + width > 1.0 is rejected (R-07).
# ----------------------------------------------------------------------------


def test_at_05_manual_crop_overflow_rejected(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)

    bad = service.change_manual_crop_settings(
        copy_id=c.value.id, x=0.6, y=0.0, width=0.5, height=0.5
    )
    assert isinstance(bad, Failure)
    assert isinstance(bad.reason, InvalidManualCropFractions)


# ----------------------------------------------------------------------------
# AT-06 — RenameImageCopy(None) succeeds (None == auto-generated name).
# ----------------------------------------------------------------------------


def test_at_06_rename_to_none_succeeds(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id, copy_name="initial")
    assert isinstance(c, Ok)
    renamed = service.rename_image_copy(copy_id=c.value.id, new_name=None)
    assert isinstance(renamed, Ok)
    assert renamed.value.copy_name is None


# ----------------------------------------------------------------------------
# AT-07 — RenameImageCopy("") fails as InvalidCopyName (R-11).
# ----------------------------------------------------------------------------


def test_at_07_rename_empty_string_rejected(
    service: ImageVariantManagementService,
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)
    renamed = service.rename_image_copy(copy_id=c.value.id, new_name="")
    assert isinstance(renamed, Failure)
    assert isinstance(renamed.reason, InvalidCopyName)


# ----------------------------------------------------------------------------
# AT-08 — CreateImageCopy with unknown asset_id returns
#         NotFound(entity_kind="ImageAsset").
# ----------------------------------------------------------------------------


def test_at_08_create_copy_unknown_asset(
    service: ImageVariantManagementService,
) -> None:
    bad = service.create_image_copy(asset_id="does-not-exist")
    assert isinstance(bad, Failure)
    assert isinstance(bad.reason, NotFound)
    assert bad.reason.entity_kind == "ImageAsset"


# ----------------------------------------------------------------------------
# AT-09 — Immediately after deleting a Copy, ImageCopyExists returns False.
# ----------------------------------------------------------------------------


def test_at_09_exists_false_after_delete(
    service: ImageVariantManagementService, event_bus: EventBus
) -> None:
    r = service.import_image_asset(
        image_bytes=b"<png-100x80>", original_filename="a.png", mime_type="image/png"
    )
    assert isinstance(r, Ok)
    c = service.create_image_copy(asset_id=r.value.id)
    assert isinstance(c, Ok)
    copy_id = c.value.id

    assert isinstance(service.image_copy_exists(copy_id=copy_id), Ok)
    assert service.image_copy_exists(copy_id=copy_id).value is True  # type: ignore[union-attr]

    service.delete_image_copy(copy_id=copy_id)
    after = service.image_copy_exists(copy_id=copy_id)
    assert isinstance(after, Ok) and after.value is False
    # And ImageCopyDeleted was emitted (downstream Capabilities depend on this).
    assert any(isinstance(e, ImageCopyDeleted) for e in event_bus.recorded)


# ----------------------------------------------------------------------------
# AT-10 — 1000-step random walk preserves invariants R-02, R-03, R-06, R-07.
#         (Implementation in test_random_walk.py — this is the contract.)
# ----------------------------------------------------------------------------

from .test_random_walk import test_random_walk_1000_steps  # noqa: F401, E402


def test_at_10_random_walk_runs(
    service: ImageVariantManagementService,
) -> None:
    """Sanity check that the random walk test exists and runs deterministically
    when seeded. The actual invariant verification is inside
    ``test_random_walk_1000_steps`` itself.
    """

    # Just verify that running a small walk with seed=42 does not raise.
    from .test_random_walk import RandomWalk

    walker = RandomWalk(seed=42)
    walker.run(n_steps=50)
