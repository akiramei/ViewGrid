"""ImageVariantManagementUseCases — UC-01..UC-17 (C-UC-CONTAINER naming).

Crucially, this class itself SATISFIES shared.ports.ImageCopyExistencePort:
it exposes `exists(copy_id: uuid.UUID) -> bool` which simply runs UC-16
(image_copy_exists) internally. That is what lets it be injected directly into
GridCompositionUseCases with NO adapter (C-BOUNDARY-IFACE).

Rules:
  - R-01 / R-02: UC-01 (decode + hash dedup).
  - R-03: UC-05 checks AssetExists; ImageCopy construction requires asset_id.
  - R-04..R-07, R-09..R-11: enforced at value-object / entity construction; the
    UseCase layer translates those construction failures into canonical Invalid*.
  - R-08: declaration only -- NO override applied here (AT-04 confirms coexistence).
  - cascade_decision NOT owned: UC-02 refuses with DependentCopiesExist.
"""

from __future__ import annotations

import uuid
from datetime import datetime, timezone

from image_variant_management import events as ev
from image_variant_management.domain import (
    Alignment,
    AutoCropSettings,
    ImageAsset,
    ImageCopy,
    ImageTransform,
    ManualCropFraction,
    Rotation,
    ScalingMode,
    SourceType,
)
from image_variant_management.failures import (
    DependentCopiesExist,
    InvalidAutoCropSettings,
    InvalidCopyName,
    InvalidImageData,
    InvalidManualCropFractions,
    InvalidOccupySize,
    NotFound,
    UnsupportedMimeType,
)
from image_variant_management.repositories import (
    ImageDecoder,
    InMemoryBlobStorage,
    InMemoryImageAssetRepository,
    InMemoryImageCopyRepository,
    fake_header_decoder,
    sha256_hex,
)
from shared.eventbus import RecordingBus
from shared.result import Err, Ok, Result
from shared.value_objects import OccupySize

SUPPORTED_MIME_TYPES = ("image/png", "image/jpeg", "image/webp", "image/x-fake")

# Sentinel so callers can pass `copy_name=None` explicitly vs. "use default".
_UNSET = object()


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


class ImageVariantManagementUseCases:
    def __init__(
        self,
        asset_repo: InMemoryImageAssetRepository,
        copy_repo: InMemoryImageCopyRepository,
        blob_storage: InMemoryBlobStorage,
        bus: RecordingBus,
        decoder: ImageDecoder | None = None,
    ) -> None:
        self._assets = asset_repo
        self._copies = copy_repo
        self._blobs = blob_storage
        self._bus = bus
        self._decode = decoder or fake_header_decoder

    # =====================================================================
    # Port satisfaction (C-BOUNDARY-IFACE): this IS ImageCopyExistencePort.
    # =====================================================================
    def exists(self, copy_id: uuid.UUID) -> bool:
        """ImageCopyExistencePort.exists — runs UC-16 internally. No adapter."""
        return self.image_copy_exists(copy_id)

    # ------------------------------------------------------------------ UC-01
    def import_image_asset(
        self,
        image_bytes: bytes,
        original_filename: str | None,
        mime_type: str,
        source_type: SourceType = SourceType.LocalFile,
    ) -> Result[uuid.UUID, object]:
        if mime_type not in SUPPORTED_MIME_TYPES:
            return Err(UnsupportedMimeType(
                attempted_mime_type=mime_type,
                supported_mime_types=SUPPORTED_MIME_TYPES))

        # R-01: decode to obtain PixelSize.
        try:
            size = self._decode(image_bytes)
        except ValueError as exc:
            return Err(InvalidImageData(detail=str(exc)))

        # R-02: hash dedup. Existing hash -> return existing, emit Duplicate.
        file_hash = sha256_hex(image_bytes)
        existing = self._assets.find_by_hash(file_hash)
        if existing is not None:
            self._bus.publish(ev.ImageAssetImportedAsDuplicate(
                existing_asset_id=existing.id, attempted_hash=file_hash))
            return Ok(existing.id)

        rel_path = self._blobs.store(image_bytes, file_hash)
        asset = ImageAsset(
            id=uuid.uuid4(),
            source_type=source_type,
            original_filename=original_filename,
            stored_relative_path=rel_path,
            size=size,
            file_hash=file_hash,
            file_size_bytes=len(image_bytes),
            mime_type=mime_type,
            created_at=_utc_now(),
        )
        self._assets.save(asset)
        self._bus.publish(ev.ImageAssetImported(asset_id=asset.id, snapshot=asset.snapshot()))
        return Ok(asset.id)

    # ------------------------------------------------------------------ UC-02
    def delete_image_asset(
        self, asset_id: uuid.UUID
    ) -> Result[uuid.UUID, object]:
        asset = self._assets.get_by_id(asset_id)
        if asset is None:
            return Err(NotFound(entity_kind="ImageAsset", entity_id=asset_id))

        dependents = self._copies.get_by_asset_id(asset_id)
        if dependents:
            # cascade_decision is NOT owned here: refuse, never auto-cascade.
            return Err(DependentCopiesExist(
                asset_id=asset_id,
                dependent_copy_ids=tuple(c.id for c in dependents)))

        snapshot = asset.snapshot()
        self._assets.delete(asset_id)
        self._blobs.delete(asset.stored_relative_path)
        self._bus.publish(ev.ImageAssetDeleted(asset_id=asset_id, snapshot_before=snapshot))
        return Ok(asset_id)

    # ------------------------------------------------------------------ UC-03
    def list_image_assets(self) -> list[ImageAsset]:
        return self._assets.list_all()

    # ------------------------------------------------------------------ UC-04
    def get_image_asset(
        self, asset_id: uuid.UUID
    ) -> Result[ImageAsset, NotFound]:
        asset = self._assets.get_by_id(asset_id)
        if asset is None:
            return Err(NotFound(entity_kind="ImageAsset", entity_id=asset_id))
        return Ok(asset)

    # ------------------------------------------------------------------ UC-05
    def create_image_copy(
        self,
        asset_id: uuid.UUID,
        copy_name: str | None = None,
        initial_transform: ImageTransform | None = None,
        initial_scaling_mode: ScalingMode = ScalingMode.UniformContain,
        initial_alignment: Alignment = Alignment.MiddleCenter,
        initial_occupy_size: OccupySize | None = None,
    ) -> Result[uuid.UUID, object]:
        # R-03: asset must exist.
        if not self._assets.get_by_id(asset_id):
            return Err(NotFound(entity_kind="ImageAsset", entity_id=asset_id))

        # R-11: copy_name null OK, empty string rejected.
        if copy_name is not None and (not isinstance(copy_name, str) or copy_name == ""):
            return Err(InvalidCopyName(
                detail="copy_name must be None or non-empty",
                attempted_value=copy_name if isinstance(copy_name, str) else None))

        transform = initial_transform if initial_transform is not None else ImageTransform()
        occupy = initial_occupy_size if initial_occupy_size is not None else OccupySize(1, 1)
        if not isinstance(occupy, OccupySize):
            return Err(InvalidOccupySize(attempted_width=None, attempted_height=None))

        try:
            copy = ImageCopy(
                id=uuid.uuid4(),
                asset_id=asset_id,
                copy_name=copy_name,
                transform=transform,
                scaling_mode=initial_scaling_mode,
                alignment=initial_alignment,
                default_occupy_size=occupy,
                auto_crop=None,
                manual_crop=None,
                created_at=_utc_now(),
                updated_at=_utc_now(),
            )
        except (TypeError, ValueError) as exc:
            # Construction enforces R-04/R-05/R-09/R-10/R-11; map to canonical.
            return Err(InvalidCopyName(detail=str(exc), attempted_value=None))

        self._copies.save(copy)
        self._bus.publish(ev.ImageCopyCreated(copy_id=copy.id, snapshot=copy.snapshot()))
        return Ok(copy.id)

    # ------------------------------------------------------------------ UC-06
    def delete_image_copy(
        self, copy_id: uuid.UUID
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        snapshot = copy.snapshot()
        self._copies.delete(copy_id)
        self._bus.publish(ev.ImageCopyDeleted(copy_id=copy_id, snapshot_before=snapshot))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-07
    def list_image_copies(
        self, asset_id: uuid.UUID | None = None
    ) -> list[ImageCopy]:
        if asset_id is None:
            return self._copies.list_all()
        return self._copies.get_by_asset_id(asset_id)

    # ------------------------------------------------------------------ UC-08
    def get_image_copy(
        self, copy_id: uuid.UUID
    ) -> Result[ImageCopy, NotFound]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        return Ok(copy)

    # ------------------------------------------------------------------ UC-09
    def change_copy_transform(
        self, copy_id: uuid.UUID, new_transform: ImageTransform
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_transform, ImageTransform):
            from image_variant_management.failures import InvalidTransform
            return Err(InvalidTransform(
                attempted_rotation=str(new_transform), attempted_flip_x=False,
                attempted_flip_y=False))
        before = copy.transform
        updated = copy.with_changes(transform=new_transform)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyTransformChanged(
            copy_id=copy_id, before=before, after=new_transform))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-10
    def change_scaling_mode(
        self, copy_id: uuid.UUID, new_scaling_mode: ScalingMode
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_scaling_mode, ScalingMode):
            from image_variant_management.failures import InvalidScalingMode
            return Err(InvalidScalingMode(attempted_value=str(new_scaling_mode)))
        before = copy.scaling_mode
        updated = copy.with_changes(scaling_mode=new_scaling_mode)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyScalingModeChanged(
            copy_id=copy_id, before=before, after=new_scaling_mode))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-11
    def change_alignment(
        self, copy_id: uuid.UUID, new_alignment: Alignment
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_alignment, Alignment):
            from image_variant_management.failures import InvalidAlignment
            return Err(InvalidAlignment(attempted_value=str(new_alignment)))
        before = copy.alignment
        updated = copy.with_changes(alignment=new_alignment)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyAlignmentChanged(
            copy_id=copy_id, before=before, after=new_alignment))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-12
    def change_auto_crop_settings(
        self,
        copy_id: uuid.UUID,
        target_color_argb: int | None,
        threshold: int | None,
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))

        # R-06: both null (OFF) or both non-null.
        if target_color_argb is None and threshold is None:
            new_auto = None
        elif target_color_argb is not None and threshold is not None:
            try:
                new_auto = AutoCropSettings(target_color_argb, threshold)
            except (TypeError, ValueError) as exc:
                return Err(InvalidAutoCropSettings(
                    detail=str(exc), attempted_target_color=target_color_argb,
                    attempted_threshold=threshold))
        else:
            return Err(InvalidAutoCropSettings(
                detail="target_color and threshold must both be set or both be null (R-06)",
                attempted_target_color=target_color_argb, attempted_threshold=threshold))

        before = copy.auto_crop
        updated = copy.with_changes(auto_crop=new_auto)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyAutoCropChanged(
            copy_id=copy_id, before=before, after=new_auto))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-13
    def change_manual_crop_settings(
        self,
        copy_id: uuid.UUID,
        x: float | None,
        y: float | None,
        width: float | None,
        height: float | None,
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))

        vals = (x, y, width, height)
        # R-07: all null (OFF) or all non-null.
        if all(v is None for v in vals):
            new_manual = None
        elif all(v is not None for v in vals):
            try:
                new_manual = ManualCropFraction(x, y, width, height)
            except (TypeError, ValueError) as exc:
                return Err(InvalidManualCropFractions(
                    detail=str(exc), x=x, y=y, width=width, height=height))
        else:
            return Err(InvalidManualCropFractions(
                detail="all four values must be set or all null (R-07)",
                x=x, y=y, width=width, height=height))

        before = copy.manual_crop
        updated = copy.with_changes(manual_crop=new_manual)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyManualCropChanged(
            copy_id=copy_id, before=before, after=new_manual))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-14
    def change_default_occupy_size(
        self, copy_id: uuid.UUID, new_occupy_size: OccupySize
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_occupy_size, OccupySize):
            return Err(InvalidOccupySize(attempted_width=None, attempted_height=None))
        before = copy.default_occupy_size
        updated = copy.with_changes(default_occupy_size=new_occupy_size)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyDefaultOccupySizeChanged(
            copy_id=copy_id, before=before, after=new_occupy_size))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-15
    def rename_image_copy(
        self, copy_id: uuid.UUID, new_name: str | None
    ) -> Result[uuid.UUID, object]:
        copy = self._copies.get_by_id(copy_id)
        if copy is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        # R-11: null OK (back to auto name), empty string rejected.
        if new_name is not None and (not isinstance(new_name, str) or new_name == ""):
            return Err(InvalidCopyName(
                detail="copy_name must be None or non-empty (R-11)",
                attempted_value=new_name if isinstance(new_name, str) else None))
        before = copy.copy_name
        updated = copy.with_changes(copy_name=new_name)
        self._copies.save(updated)
        self._bus.publish(ev.ImageCopyRenamed(copy_id=copy_id, before=before, after=new_name))
        return Ok(copy_id)

    # ------------------------------------------------------------------ UC-16
    def image_copy_exists(self, copy_id: uuid.UUID) -> bool:
        return self._copies.exists(copy_id)

    # ------------------------------------------------------------------ UC-17
    def image_asset_exists(self, asset_id: uuid.UUID) -> bool:
        return self._assets.get_by_id(asset_id) is not None
