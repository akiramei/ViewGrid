"""IMAGE_VARIANT_MANAGEMENT UseCases (C-UC-CONTAINER: ImageVariantManagementUseCases).

Implements UC-01..UC-17.

Natively satisfies two shared ports (no standalone adapters):
  - ImageCopyExistencePort.exists  (n=2 existing boundary; runs UC-16 logic)
  - CopyRenderSpecPort.get_copy_render_spec  (v0.3 PRE-LOADED read projection)

R-08 (ManualCropOverridesAutoCrop) is declaration-only here: this Capability
stores both auto_crop and manual_crop and never applies a priority.

Local MUST_DECIDE: image decoder + hash are pluggable callables (defaults
provided) -- decoder/hash implementation is Capability-local, not contract-fixed.
"""
from __future__ import annotations

import hashlib
import uuid
from dataclasses import replace
from typing import Callable

from shared.events import EventBus, NullBus
from shared.render_contracts import CopyRenderSpec
from shared.result import Err, Ok, Result
from shared.value_objects import OccupySize, PixelSize
from . import events as ev
from . import failures as fail
from .domain import (
    AutoCropSettings,
    ImageAsset,
    ImageCopy,
    ImageTransform,
    ManualCropFraction,
)
from .enums import Alignment, Rotation, ScalingMode
from .repositories import InMemoryImageAssetRepository, InMemoryImageCopyRepository

SUPPORTED_MIME_TYPES = ("image/png", "image/jpeg", "image/webp", "image/bmp")


# --- Local MUST_DECIDE: decoder + hash (Capability-local, not contract-fixed) ---
def _default_decoder(image_bytes: bytes, mime_type: str) -> PixelSize:
    """Trivial decoder for the PoC: derives a deterministic size from bytes.

    A real implementation would decode the image. Here we just require
    non-empty bytes (raise on empty to exercise R-01) and produce a size.
    """
    if not image_bytes:
        raise ValueError("empty image data")
    w = (len(image_bytes) % 1000) + 1
    h = ((len(image_bytes) // 1000) % 1000) + 1
    return PixelSize(width=w, height=h)


def _default_hasher(image_bytes: bytes) -> str:
    return hashlib.sha256(image_bytes).hexdigest()


def _copy_snapshot(c: ImageCopy) -> dict:
    return {
        "id": c.id,
        "asset_id": c.asset_id,
        "copy_name": c.copy_name,
        "rotation": c.transform.rotation.value,
        "flip_x": c.transform.flip_x,
        "flip_y": c.transform.flip_y,
        "scaling_mode": c.scaling_mode.value,
        "alignment": c.alignment.value,
        "default_occupy_size": (c.default_occupy_size.width, c.default_occupy_size.height),
        "auto_crop": (c.auto_crop.target_color_argb, c.auto_crop.threshold) if c.auto_crop else None,
        "manual_crop": (
            (c.manual_crop.x, c.manual_crop.y, c.manual_crop.width, c.manual_crop.height)
            if c.manual_crop
            else None
        ),
    }


def _asset_snapshot(a: ImageAsset) -> dict:
    return {
        "id": a.id,
        "source_type": a.source_type,
        "original_filename": a.original_filename,
        "stored_relative_path": a.stored_relative_path,
        "size": (a.size.width, a.size.height),
        "file_hash": a.file_hash,
        "file_size_bytes": a.file_size_bytes,
        "mime_type": a.mime_type,
    }


class ImageVariantManagementUseCases:
    def __init__(
        self,
        asset_repo: InMemoryImageAssetRepository | None = None,
        copy_repo: InMemoryImageCopyRepository | None = None,
        bus: EventBus | None = None,
        decoder: Callable[[bytes, str], PixelSize] | None = None,
        hasher: Callable[[bytes], str] | None = None,
    ) -> None:
        self._assets = asset_repo or InMemoryImageAssetRepository()
        self._copies = copy_repo or InMemoryImageCopyRepository()
        self._bus = bus or NullBus()
        self._decode = decoder or _default_decoder
        self._hash = hasher or _default_hasher

    # ------------------------------------------------------------------
    # UC-01 ImportImageAsset
    # ------------------------------------------------------------------
    def import_image_asset(
        self,
        image_bytes: bytes,
        original_filename: str | None,
        mime_type: str,
        source_type: str = "LocalFile",
    ) -> Result[ImageAsset, object]:
        if mime_type not in SUPPORTED_MIME_TYPES:
            return Err(
                fail.UnsupportedMimeType(
                    attempted_mime_type=mime_type,
                    supported_mime_types=SUPPORTED_MIME_TYPES,
                )
            )
        # R-01: decode to verify valid image data + get pixel size.
        try:
            size = self._decode(image_bytes, mime_type)
        except Exception as exc:  # noqa: BLE001
            return Err(fail.InvalidImageData(detail=str(exc)))
        # R-02: SHA-256 hash dedup -> return existing if present.
        file_hash = self._hash(image_bytes)
        existing = self._assets.find_by_hash(file_hash)
        if existing is not None:
            self._bus.publish(
                ev.ImageAssetImportedAsDuplicate(
                    existing_asset_id=existing.id, attempted_hash=file_hash
                )
            )
            return Ok(existing)
        asset = ImageAsset(
            id=uuid.uuid4(),
            source_type=source_type,
            original_filename=original_filename,
            stored_relative_path=f"assets/{file_hash}",
            size=size,
            file_hash=file_hash,
            file_size_bytes=len(image_bytes),
            mime_type=mime_type,
        )
        self._assets.save(asset)
        self._bus.publish(ev.ImageAssetImported(asset_id=asset.id, snapshot=_asset_snapshot(asset)))
        return Ok(asset)

    # ------------------------------------------------------------------
    # UC-02 DeleteImageAsset
    # ------------------------------------------------------------------
    def delete_image_asset(self, asset_id: uuid.UUID) -> Result[uuid.UUID, object]:
        asset = self._assets.get_by_id(asset_id)
        if asset is None:
            return Err(fail.NotFound(entity_kind="ImageAsset", entity_id=asset_id))
        dependents = self._copies.get_by_asset_id(asset_id)
        if dependents:
            return Err(
                fail.DependentCopiesExist(
                    asset_id=asset_id,
                    dependent_copy_ids=tuple(c.id for c in dependents),
                )
            )
        snapshot = _asset_snapshot(asset)
        self._assets.delete(asset_id)
        self._bus.publish(ev.ImageAssetDeleted(asset_id=asset_id, snapshot_before=snapshot))
        return Ok(asset_id)

    # ------------------------------------------------------------------
    # UC-03 ListImageAssets / UC-04 GetImageAsset
    # ------------------------------------------------------------------
    def list_image_assets(self) -> list[ImageAsset]:
        return self._assets.list_all()

    def get_image_asset(self, asset_id: uuid.UUID) -> Result[ImageAsset, object]:
        asset = self._assets.get_by_id(asset_id)
        if asset is None:
            return Err(fail.NotFound(entity_kind="ImageAsset", entity_id=asset_id))
        return Ok(asset)

    # ------------------------------------------------------------------
    # UC-05 CreateImageCopy
    # ------------------------------------------------------------------
    def create_image_copy(
        self,
        asset_id: uuid.UUID,
        copy_name: str | None = None,
        initial_transform: ImageTransform | None = None,
        initial_scaling_mode: ScalingMode | None = None,
        initial_alignment: Alignment | None = None,
        initial_occupy_size: OccupySize | None = None,
    ) -> Result[ImageCopy, object]:
        if self._assets.get_by_id(asset_id) is None:
            return Err(fail.NotFound(entity_kind="ImageAsset", entity_id=asset_id))
        if copy_name is not None and not isinstance(copy_name, str):
            return Err(fail.InvalidCopyName(detail="copy_name must be a string or None", attempted_value=None))
        if copy_name == "":
            return Err(fail.InvalidCopyName(detail="copy_name must not be empty", attempted_value=""))

        transform = initial_transform or ImageTransform()
        if not isinstance(transform, ImageTransform) or not isinstance(transform.rotation, Rotation):
            return Err(
                fail.InvalidTransform(
                    attempted_rotation=str(getattr(transform, "rotation", None)),
                    attempted_flip_x=bool(getattr(transform, "flip_x", False)),
                    attempted_flip_y=bool(getattr(transform, "flip_y", False)),
                )
            )
        scaling_mode = initial_scaling_mode or ScalingMode.UniformContain
        if not isinstance(scaling_mode, ScalingMode):
            return Err(fail.InvalidScalingMode(attempted_value=str(scaling_mode)))
        alignment = initial_alignment or Alignment.MiddleCenter
        if not isinstance(alignment, Alignment):
            return Err(fail.InvalidAlignment(attempted_value=str(alignment)))
        occupy = initial_occupy_size or OccupySize(1, 1)
        if not isinstance(occupy, OccupySize):
            return Err(fail.InvalidOccupySize(attempted_width=-1, attempted_height=-1))

        copy = ImageCopy(
            id=uuid.uuid4(),
            asset_id=asset_id,
            copy_name=copy_name,
            transform=transform,
            scaling_mode=scaling_mode,
            alignment=alignment,
            default_occupy_size=occupy,
            auto_crop=None,
            manual_crop=None,
        )
        self._copies.save(copy)
        self._bus.publish(ev.ImageCopyCreated(copy_id=copy.id, snapshot=_copy_snapshot(copy)))
        return Ok(copy)

    # ------------------------------------------------------------------
    # UC-06 DeleteImageCopy
    # ------------------------------------------------------------------
    def delete_image_copy(self, copy_id: uuid.UUID) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        snapshot = _copy_snapshot(copy)
        self._copies.delete(copy_id)
        self._bus.publish(ev.ImageCopyDeleted(copy_id=copy_id, snapshot_before=snapshot))
        return Ok(copy_id)

    # ------------------------------------------------------------------
    # UC-07 ListImageCopies / UC-08 GetImageCopy
    # ------------------------------------------------------------------
    def list_image_copies(self, asset_id: uuid.UUID | None = None) -> list[ImageCopy]:
        if asset_id is None:
            return self._copies.list_all()
        return self._copies.get_by_asset_id(asset_id)

    def get_image_copy(self, copy_id: uuid.UUID) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        return Ok(copy)

    # ------------------------------------------------------------------
    # UC-09 ChangeCopyTransform
    # ------------------------------------------------------------------
    def change_copy_transform(
        self, copy_id: uuid.UUID, new_transform: ImageTransform
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_transform, ImageTransform) or not isinstance(
            new_transform.rotation, Rotation
        ):
            return Err(
                fail.InvalidTransform(
                    attempted_rotation=str(getattr(new_transform, "rotation", None)),
                    attempted_flip_x=bool(getattr(new_transform, "flip_x", False)),
                    attempted_flip_y=bool(getattr(new_transform, "flip_y", False)),
                )
            )
        before = copy.transform
        updated = replace(copy, transform=new_transform)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyTransformChanged(copy_id=copy_id, before=before, after=new_transform))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-10 ChangeScalingMode
    # ------------------------------------------------------------------
    def change_scaling_mode(
        self, copy_id: uuid.UUID, new_scaling_mode: ScalingMode
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_scaling_mode, ScalingMode):
            return Err(fail.InvalidScalingMode(attempted_value=str(new_scaling_mode)))
        before = copy.scaling_mode
        updated = replace(copy, scaling_mode=new_scaling_mode)
        self._copies.save(updated)
        self._bus.publish(
            ev.ImageCopyScalingModeChanged(copy_id=copy_id, before=before, after=new_scaling_mode)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-11 ChangeAlignment
    # ------------------------------------------------------------------
    def change_alignment(
        self, copy_id: uuid.UUID, new_alignment: Alignment
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_alignment, Alignment):
            return Err(fail.InvalidAlignment(attempted_value=str(new_alignment)))
        before = copy.alignment
        updated = replace(copy, alignment=new_alignment)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyAlignmentChanged(copy_id=copy_id, before=before, after=new_alignment))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-12 ChangeAutoCropSettings (R-06)
    # ------------------------------------------------------------------
    def change_auto_crop_settings(
        self, copy_id: uuid.UUID, target_color_argb: int | None, threshold: int | None
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        both_null = target_color_argb is None and threshold is None
        both_set = target_color_argb is not None and threshold is not None
        if not (both_null or both_set):
            return Err(
                fail.InvalidAutoCropSettings(
                    detail="target_color and threshold must be both null or both set",
                    attempted_target_color=target_color_argb,
                    attempted_threshold=threshold,
                )
            )
        new_auto: AutoCropSettings | None
        if both_null:
            new_auto = None
        else:
            try:
                new_auto = AutoCropSettings(
                    target_color_argb=target_color_argb, threshold=threshold
                )
            except (ValueError, TypeError) as exc:
                return Err(
                    fail.InvalidAutoCropSettings(
                        detail=str(exc),
                        attempted_target_color=target_color_argb,
                        attempted_threshold=threshold,
                    )
                )
        before = copy.auto_crop
        updated = replace(copy, auto_crop=new_auto)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyAutoCropChanged(copy_id=copy_id, before=before, after=new_auto))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-13 ChangeManualCropSettings (R-07)
    # ------------------------------------------------------------------
    def change_manual_crop_settings(
        self,
        copy_id: uuid.UUID,
        x: float | None,
        y: float | None,
        width: float | None,
        height: float | None,
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        vals = (x, y, width, height)
        all_null = all(v is None for v in vals)
        all_set = all(v is not None for v in vals)
        if not (all_null or all_set):
            return Err(
                fail.InvalidManualCropFractions(
                    detail="all four values must be null or all set",
                    x=x, y=y, width=width, height=height,
                )
            )
        new_manual: ManualCropFraction | None
        if all_null:
            new_manual = None
        else:
            try:
                new_manual = ManualCropFraction(x=x, y=y, width=width, height=height)
            except (ValueError, TypeError) as exc:
                return Err(
                    fail.InvalidManualCropFractions(
                        detail=str(exc), x=x, y=y, width=width, height=height
                    )
                )
        before = copy.manual_crop
        updated = replace(copy, manual_crop=new_manual)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyManualCropChanged(copy_id=copy_id, before=before, after=new_manual))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-14 ChangeDefaultOccupySize
    # ------------------------------------------------------------------
    def change_default_occupy_size(
        self, copy_id: uuid.UUID, new_occupy_size: OccupySize
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_occupy_size, OccupySize):
            return Err(fail.InvalidOccupySize(attempted_width=-1, attempted_height=-1))
        before = copy.default_occupy_size
        updated = replace(copy, default_occupy_size=new_occupy_size)
        self._copies.save(updated)
        self._bus.publish(
            ev.ImageCopyDefaultOccupySizeChanged(copy_id=copy_id, before=before, after=new_occupy_size)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-15 RenameImageCopy (R-11)
    # ------------------------------------------------------------------
    def rename_image_copy(
        self, copy_id: uuid.UUID, new_name: str | None
    ) -> Result[ImageCopy, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if new_name is not None and not isinstance(new_name, str):
            return Err(fail.InvalidCopyName(detail="name must be str or None", attempted_value=None))
        if new_name == "":
            return Err(fail.InvalidCopyName(detail="name must not be empty", attempted_value=""))
        before = copy.copy_name
        updated = replace(copy, copy_name=new_name)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyRenamed(copy_id=copy_id, before=before, after=new_name))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-16 ImageCopyExists / UC-17 ImageAssetExists
    # ------------------------------------------------------------------
    def image_copy_exists(self, copy_id: uuid.UUID) -> bool:
        return self._copies.exists(copy_id)

    def image_asset_exists(self, asset_id: uuid.UUID) -> bool:
        return self._assets.get_by_id(asset_id) is not None

    # ------------------------------------------------------------------
    # C-BOUNDARY-IFACE (n=2): satisfies ImageCopyExistencePort natively.
    # ------------------------------------------------------------------
    def exists(self, copy_id: uuid.UUID) -> bool:
        return self.image_copy_exists(copy_id)

    # ------------------------------------------------------------------
    # C-CONSUMER-PORTS (v0.3, pre-loaded): CopyRenderSpecPort.
    # IMGVAR natively satisfies the read port -- no standalone adapter.
    # enums projected to neutral str via .value. R-08 NOT applied here.
    # ------------------------------------------------------------------
    def get_copy_render_spec(self, copy_id: uuid.UUID) -> CopyRenderSpec | None:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return None
        auto = (
            (copy.auto_crop.target_color_argb, copy.auto_crop.threshold)
            if copy.auto_crop is not None
            else None
        )
        manual = (
            (copy.manual_crop.x, copy.manual_crop.y, copy.manual_crop.width, copy.manual_crop.height)
            if copy.manual_crop is not None
            else None
        )
        return CopyRenderSpec(
            rotation=copy.transform.rotation.value,
            flip_x=copy.transform.flip_x,
            flip_y=copy.transform.flip_y,
            scaling_mode=copy.scaling_mode.value,
            alignment=copy.alignment.value,
            auto_crop=auto,
            manual_crop=manual,
        )
