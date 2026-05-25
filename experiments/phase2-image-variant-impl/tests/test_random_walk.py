"""1000-step property-based random walk (30-design.md §6.2).

Invariants checked AFTER EVERY STEP:

* R-02 — Hash and Asset are 1:1 (no duplicate hashes).
* R-03 — Every ImageCopy references an existing ImageAsset.
* R-06 — Every ImageCopy.auto_crop is None or a fully-populated AutoCropSettings.
* R-07 — Every ImageCopy.manual_crop is None or a fully-populated ManualCropFraction.
* "No orphaned blob" — On ImageAssetDeleted, the blob is gone from storage.

The walker exercises ALL state-changing UseCases (UC-01 .. UC-06, UC-09 .. UC-15)
with random parameter choices. Both valid and invalid inputs are emitted; the
walker treats all Failure outcomes as expected (the Capability defending its
own invariants).
"""

from __future__ import annotations

import random
from datetime import datetime, timezone
from itertools import count
from typing import Callable, List, Optional

from image_variant_management import (
    Alignment,
    AutoCropSettings,
    EventBus,
    Failure,
    ImageAssetDeleted,
    ImageBlobStorage,
    ImageCopy,
    ImageCopyDeleted,
    ImageTransform,
    ImageVariantManagementService,
    InMemoryImageAssetRepository,
    InMemoryImageBlobStorage,
    InMemoryImageCopyRepository,
    ManualCropFraction,
    MockImageDecoder,
    OccupySize,
    Ok,
    PixelSize,
    Rotation,
    ScalingMode,
    SourceType,
)


N_STEPS_DEFAULT = 1000


class RandomWalk:
    """Pseudo-random walker over the IMAGE_VARIANT_MANAGEMENT UseCases.

    The walker is deterministic given a seed and a fixed action distribution.
    Invariants are checked after every step; an assertion failure aborts the
    walk with a useful trace.
    """

    def __init__(self, seed: int = 42) -> None:
        self.rng = random.Random(seed)

        # Fixed pool of distinct "byte payloads" the decoder knows about.
        self.blob_pool: List[bytes] = []
        decoder_table: dict[bytes, PixelSize] = {}
        for i in range(20):
            blob = f"blob-{i:03d}".encode("utf-8")
            self.blob_pool.append(blob)
            decoder_table[blob] = PixelSize(width=10 + i, height=10 + i)
        self.decoder = MockImageDecoder(table=decoder_table)

        self.asset_repo = InMemoryImageAssetRepository()
        self.copy_repo = InMemoryImageCopyRepository()
        self.blob_storage = InMemoryImageBlobStorage()
        self.bus = EventBus()
        self.deleted_blob_paths: list[str] = []

        def _on_event(e: object) -> None:
            if isinstance(e, ImageAssetDeleted):
                # Record what blob path *should* be gone.
                self.deleted_blob_paths.append(e.snapshot_before.stored_relative_path)

        self.bus.subscribe(_on_event)

        counter = count(start=1)

        def _id() -> str:
            return f"walk-{next(counter):06d}"

        def _clock() -> datetime:
            return datetime(2026, 1, 1, 0, 0, next(counter) % 60, tzinfo=timezone.utc)

        self.service = ImageVariantManagementService(
            asset_repo=self.asset_repo,
            copy_repo=self.copy_repo,
            blob_storage=self.blob_storage,
            decoder=self.decoder,
            event_bus=self.bus,
            clock=_clock,
            id_factory=_id,
        )

    # ------------------------------------------------------------------
    # Invariant check
    # ------------------------------------------------------------------

    def assert_invariants(self, step: int, action: str) -> None:
        # R-02: hash uniqueness across all stored ImageAssets.
        assets = self.asset_repo.list_all()
        hashes = [a.file_hash for a in assets]
        assert len(hashes) == len(set(hashes)), (
            f"step={step} action={action}: R-02 violated, duplicate hash detected"
        )

        # R-03: every ImageCopy references an existing asset.
        copies = self.copy_repo.list_all()
        asset_ids = {a.id for a in assets}
        for c in copies:
            assert c.asset_id in asset_ids, (
                f"step={step} action={action}: R-03 violated, copy {c.id} references missing asset {c.asset_id}"
            )

        # R-06: AutoCropSettings is None or a valid value object.
        for c in copies:
            if c.auto_crop is not None:
                assert isinstance(c.auto_crop, AutoCropSettings)
                # both fields populated by virtue of construction
                assert isinstance(c.auto_crop.target_color_argb, int)
                assert 0 <= c.auto_crop.threshold <= 255

        # R-07: ManualCropFraction is None or a valid value object.
        for c in copies:
            if c.manual_crop is not None:
                assert isinstance(c.manual_crop, ManualCropFraction)
                assert 0.0 <= c.manual_crop.x <= 1.0
                assert 0.0 <= c.manual_crop.y <= 1.0
                assert 0.0 <= c.manual_crop.width <= 1.0
                assert 0.0 <= c.manual_crop.height <= 1.0
                assert c.manual_crop.x + c.manual_crop.width <= 1.0 + 1e-9
                assert c.manual_crop.y + c.manual_crop.height <= 1.0 + 1e-9

        # "No orphaned blob" (30-design.md §6.2): every blob currently in
        # storage must be referenced by some live ImageAsset. (Re-imports of
        # the same bytes legitimately resurrect a path; what we forbid is a
        # blob that exists in storage but no asset claims it.)
        live_paths = {a.stored_relative_path for a in assets}
        stored_paths = set(self.blob_storage.all_paths())
        orphans = stored_paths - live_paths
        assert not orphans, (
            f"step={step} action={action}: orphaned blobs in storage: {orphans}"
        )

    # ------------------------------------------------------------------
    # Step
    # ------------------------------------------------------------------

    def step(self, step_idx: int) -> str:
        weights = {
            "import": 5,
            "delete_asset": 1,
            "create_copy": 5,
            "delete_copy": 2,
            "transform": 3,
            "scaling": 3,
            "alignment": 3,
            "auto_crop": 3,
            "manual_crop": 3,
            "occupy": 2,
            "rename": 2,
            "exists": 1,
        }
        action = self.rng.choices(list(weights.keys()), weights=list(weights.values()))[0]

        if action == "import":
            blob = self.rng.choice(self.blob_pool + [b"invalid-bytes"])
            mime = self.rng.choice(["image/png", "image/jpeg", "image/tiff"])
            self.service.import_image_asset(
                image_bytes=blob, original_filename="x.png", mime_type=mime
            )
        elif action == "delete_asset":
            assets = self.asset_repo.list_all()
            if assets:
                a = self.rng.choice(assets)
                self.service.delete_image_asset(asset_id=a.id)
            else:
                self.service.delete_image_asset(asset_id="nope")
        elif action == "create_copy":
            assets = self.asset_repo.list_all()
            if assets:
                a = self.rng.choice(assets)
                self.service.create_image_copy(
                    asset_id=a.id,
                    copy_name=self.rng.choice([None, "name", "別名", "x"]),
                )
            else:
                self.service.create_image_copy(asset_id="nope")
        elif action == "delete_copy":
            copies = self.copy_repo.list_all()
            if copies:
                c = self.rng.choice(copies)
                self.service.delete_image_copy(copy_id=c.id)
            else:
                self.service.delete_image_copy(copy_id="nope")
        elif action == "transform":
            c = self._pick_copy()
            if c is not None:
                self.service.change_copy_transform(
                    copy_id=c.id,
                    new_transform=ImageTransform(
                        rotation=self.rng.choice(list(Rotation)),
                        flip_x=self.rng.choice([True, False]),
                        flip_y=self.rng.choice([True, False]),
                    ),
                )
        elif action == "scaling":
            c = self._pick_copy()
            if c is not None:
                self.service.change_scaling_mode(
                    copy_id=c.id, new_scaling_mode=self.rng.choice(list(ScalingMode))
                )
        elif action == "alignment":
            c = self._pick_copy()
            if c is not None:
                self.service.change_alignment(
                    copy_id=c.id, new_alignment=self.rng.choice(list(Alignment))
                )
        elif action == "auto_crop":
            c = self._pick_copy()
            if c is not None:
                # Mix valid / partial-null / out-of-range to exercise R-06.
                pick = self.rng.choice(["off", "valid", "partial1", "partial2", "oob"])
                if pick == "off":
                    self.service.change_auto_crop_settings(
                        copy_id=c.id, target_color_argb=None, threshold=None
                    )
                elif pick == "valid":
                    self.service.change_auto_crop_settings(
                        copy_id=c.id,
                        target_color_argb=self.rng.randint(0, 0xFFFFFFFF),
                        threshold=self.rng.randint(0, 255),
                    )
                elif pick == "partial1":
                    self.service.change_auto_crop_settings(
                        copy_id=c.id, target_color_argb=0xFFFFFFFF, threshold=None
                    )
                elif pick == "partial2":
                    self.service.change_auto_crop_settings(
                        copy_id=c.id, target_color_argb=None, threshold=10
                    )
                else:  # oob
                    self.service.change_auto_crop_settings(
                        copy_id=c.id, target_color_argb=0xFFFFFFFF, threshold=999
                    )
        elif action == "manual_crop":
            c = self._pick_copy()
            if c is not None:
                pick = self.rng.choice(["off", "valid", "partial", "oob"])
                if pick == "off":
                    self.service.change_manual_crop_settings(
                        copy_id=c.id, x=None, y=None, width=None, height=None
                    )
                elif pick == "valid":
                    x = self.rng.uniform(0.0, 0.9)
                    y = self.rng.uniform(0.0, 0.9)
                    w = self.rng.uniform(0.05, 1.0 - x)
                    h = self.rng.uniform(0.05, 1.0 - y)
                    self.service.change_manual_crop_settings(
                        copy_id=c.id, x=x, y=y, width=w, height=h
                    )
                elif pick == "partial":
                    self.service.change_manual_crop_settings(
                        copy_id=c.id, x=0.0, y=None, width=0.5, height=0.5
                    )
                else:  # oob
                    self.service.change_manual_crop_settings(
                        copy_id=c.id, x=0.7, y=0.7, width=0.5, height=0.5
                    )
        elif action == "occupy":
            c = self._pick_copy()
            if c is not None:
                # Mix valid + invalid (zero/negative) — invalid path returns Failure.
                w = self.rng.choice([1, 2, 3, 0, -1])
                h = self.rng.choice([1, 2, 3, 0, -1])
                if w >= 1 and h >= 1:
                    self.service.change_default_occupy_size(
                        copy_id=c.id, new_occupy_size=OccupySize(width=w, height=h)
                    )
                # Skip emitting clearly-impossible OccupySize values — domain
                # would raise ValueError before reaching the UseCase, which is
                # itself a domain invariant test (covered in test_rules.py).
        elif action == "rename":
            c = self._pick_copy()
            if c is not None:
                new = self.rng.choice([None, "a", "別名", "", "x" * 20])
                self.service.rename_image_copy(copy_id=c.id, new_name=new)
        elif action == "exists":
            cid = self.rng.choice(
                [c.id for c in self.copy_repo.list_all()] + ["missing"]
            )
            self.service.image_copy_exists(copy_id=cid)
        return action

    def _pick_copy(self) -> Optional[ImageCopy]:
        copies = self.copy_repo.list_all()
        return self.rng.choice(copies) if copies else None

    # ------------------------------------------------------------------
    # Driver
    # ------------------------------------------------------------------

    def run(self, n_steps: int = N_STEPS_DEFAULT) -> None:
        for i in range(n_steps):
            action = self.step(i)
            self.assert_invariants(i, action)


# Public test entry points ----------------------------------------------------


def test_random_walk_1000_steps() -> None:
    """The mandatory 1000-step random walk (30-design.md §6.2 / AT-10)."""

    walker = RandomWalk(seed=42)
    walker.run(n_steps=1000)


def test_random_walk_alt_seed() -> None:
    """Additional random walk with a different seed — same invariants must hold."""

    walker = RandomWalk(seed=2026)
    walker.run(n_steps=500)
