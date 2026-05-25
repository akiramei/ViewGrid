"""UseCase layer for IMAGE_VARIANT_MANAGEMENT (UC-01..UC-17).

Each UseCase is a callable on the ``ImageVariantManagementService`` class that
takes typed inputs and returns ``Result[T]`` (an ``Ok`` with payload or a
``Failure`` with one of the canonical failure reasons).

Rule enforcement map (per 20-capability-bom.md §3):

* R-01 InvalidImageData         — UC-01 (this module, decoder probe)
* R-02 FileHashMustBeUnique     — UC-01 (this module, repository.find_by_hash)
* R-03 ImageCopyMustReferenceExistingAsset — UC-05 (this module, AssetExists)
* R-04 ScalingMode enum         — domain.ScalingMode + UC-10 validation
* R-05 Alignment enum           — domain.Alignment + UC-11 validation
* R-06 AutoCropSettings aggregate — domain.AutoCropSettings + UC-12 validation
* R-07 ManualCropFraction range — domain.ManualCropFraction + UC-13 validation
* R-08 ManualCropOverridesAutoCrop — **declaration only**, no enforcement code here.
* R-09 Rotation enum            — domain.Rotation + UC-09 validation
* R-10 OccupySize positive      — domain.OccupySize + UC-05/UC-14 validation
* R-11 CopyName null-or-nonempty — domain.ImageCopy + UC-05/UC-15 validation

Decision ownership audit (self-check):

* cascade_decision: **NOT owned here**. UC-02 only refuses with
  ``DependentCopiesExist`` when copies exist; no automatic cascade.
* rendering_decision: **NOT owned here**. UC-12 / UC-13 store both AutoCrop
  and ManualCrop independently; neither nulls the other when set.
"""

from __future__ import annotations

import hashlib
import uuid
from datetime import datetime
from typing import Callable, Optional, Sequence

from .domain import (
    Alignment,
    AutoCropSettings,
    ImageAsset,
    ImageCopy,
    ImageTransform,
    ManualCropFraction,
    Rotation,
    ScalingMode,
    SourceType,
    utc_now,
)
from .events import (
    EventBus,
    ImageAssetDeleted,
    ImageAssetImported,
    ImageAssetImportedAsDuplicate,
    ImageCopyAlignmentChanged,
    ImageCopyAutoCropChanged,
    ImageCopyCreated,
    ImageCopyDefaultOccupySizeChanged,
    ImageCopyDeleted,
    ImageCopyManualCropChanged,
    ImageCopyRenamed,
    ImageCopyScalingModeChanged,
    ImageCopyTransformChanged,
)
from .failures import (
    DependentCopiesExist,
    Failure,
    InvalidAlignment,
    InvalidAutoCropSettings,
    InvalidCopyName,
    InvalidImageData,
    InvalidManualCropFractions,
    InvalidOccupySize,
    InvalidScalingMode,
    InvalidTransform,
    NotFound,
    Ok,
    Result,
    UnsupportedMimeType,
)
from .image_decoder import ImageDecoder
from .repositories import ImageAssetRepository, ImageBlobStorage, ImageCopyRepository
from .shared import OccupySize, PixelSize


# ----------------------------------------------------------------------------
# Configuration
# ----------------------------------------------------------------------------


# Supported MIME types. The actual decode validation is in the decoder; this
# guard is a fast-path "obvious mismatch" check at the UseCase boundary.
DEFAULT_SUPPORTED_MIME_TYPES: tuple[str, ...] = (
    "image/png",
    "image/jpeg",
    "image/jpg",
    "image/gif",
    "image/bmp",
    "image/webp",
)


# ----------------------------------------------------------------------------
# Service
# ----------------------------------------------------------------------------


class ImageVariantManagementService:
    """The aggregate of all UseCases for this Capability.

    Each ``do_UC_XX`` method corresponds to one UseCase from the YAML.
    """

    def __init__(
        self,
        *,
        asset_repo: ImageAssetRepository,
        copy_repo: ImageCopyRepository,
        blob_storage: ImageBlobStorage,
        decoder: ImageDecoder,
        event_bus: EventBus,
        clock: Callable[[], datetime] = utc_now,
        id_factory: Callable[[], str] = lambda: str(uuid.uuid4()),
        supported_mime_types: Sequence[str] = DEFAULT_SUPPORTED_MIME_TYPES,
    ) -> None:
        self._asset_repo = asset_repo
        self._copy_repo = copy_repo
        self._blob_storage = blob_storage
        self._decoder = decoder
        self._bus = event_bus
        self._clock = clock
        self._id_factory = id_factory
        self._supported_mime_types = tuple(supported_mime_types)

    # ------------------------------------------------------------------
    # UC-01 ImportImageAsset
    # ------------------------------------------------------------------

    def import_image_asset(
        self,
        *,
        image_bytes: bytes,
        original_filename: Optional[str],
        mime_type: str,
        source_type: SourceType = SourceType.LOCAL_FILE,
    ) -> Result[ImageAsset]:
        # Validation: MIME type
        if mime_type not in self._supported_mime_types:
            return Failure(
                UnsupportedMimeType(
                    attempted_mime_type=mime_type,
                    supported_mime_types=self._supported_mime_types,
                )
            )

        # R-01: probe decoder.
        try:
            size = self._decoder.decode_size(image_bytes, mime_type)
        except Exception as e:  # decoder threw — treat as invalid data.
            return Failure(InvalidImageData(detail=f"decoder error: {e}"))
        if size is None:
            return Failure(InvalidImageData(detail="image bytes could not be decoded"))

        # R-02: compute SHA-256 and check for existing asset.
        file_hash = hashlib.sha256(image_bytes).hexdigest().lower()
        existing = self._asset_repo.find_by_hash(file_hash)
        if existing is not None:
            # Duplicate path: emit *only* ImageAssetImportedAsDuplicate; do NOT
            # emit ImageAssetImported. Return the existing asset wrapped in Ok.
            self._bus.publish(
                ImageAssetImportedAsDuplicate(
                    existing_asset_id=existing.id, attempted_hash=file_hash
                )
            )
            return Ok(existing)

        # New asset: store blob, build entity, persist, emit.
        relative_path = self._blob_storage.store(image_bytes, file_hash)
        asset = ImageAsset(
            id=self._id_factory(),
            source_type=source_type,
            original_filename=original_filename,
            stored_relative_path=relative_path,
            size=size,
            file_hash=file_hash,
            file_size_bytes=len(image_bytes),
            mime_type=mime_type,
            created_at=self._clock(),
        )
        self._asset_repo.save(asset)
        self._bus.publish(ImageAssetImported(asset_id=asset.id, snapshot=asset))
        return Ok(asset)

    # ------------------------------------------------------------------
    # UC-02 DeleteImageAsset
    # ------------------------------------------------------------------

    def delete_image_asset(self, *, asset_id: str) -> Result[None]:
        asset = self._asset_repo.get_by_id(asset_id)
        if asset is None:
            return Failure(NotFound(entity_kind="ImageAsset", entity_id=asset_id))

        # Cascade refusal: if any ImageCopy depends on this asset, REFUSE.
        # This Capability MUST NOT auto-cascade-delete the dependents.
        # The upper Coordinator decides what to do (Decision ownership §6).
        dependents = self._copy_repo.get_by_asset_id(asset_id)
        if dependents:
            return Failure(
                DependentCopiesExist(
                    asset_id=asset_id,
                    dependent_copy_ids=tuple(c.id for c in dependents),
                )
            )

        # Safe to delete: remove entity + blob, emit event.
        self._asset_repo.delete(asset_id)
        self._blob_storage.delete(asset.stored_relative_path)
        self._bus.publish(ImageAssetDeleted(asset_id=asset_id, snapshot_before=asset))
        return Ok(None)

    # ------------------------------------------------------------------
    # UC-03 ListImageAssets
    # ------------------------------------------------------------------

    def list_image_assets(self) -> Result[list[ImageAsset]]:
        return Ok(self._asset_repo.list_all())

    # ------------------------------------------------------------------
    # UC-04 GetImageAsset
    # ------------------------------------------------------------------

    def get_image_asset(self, *, asset_id: str) -> Result[ImageAsset]:
        asset = self._asset_repo.get_by_id(asset_id)
        if asset is None:
            return Failure(NotFound(entity_kind="ImageAsset", entity_id=asset_id))
        return Ok(asset)

    # ------------------------------------------------------------------
    # UC-05 CreateImageCopy
    # ------------------------------------------------------------------

    def create_image_copy(
        self,
        *,
        asset_id: str,
        copy_name: Optional[str] = None,
        initial_transform: Optional[ImageTransform] = None,
        initial_scaling_mode: Optional[ScalingMode] = None,
        initial_alignment: Optional[Alignment] = None,
        initial_occupy_size: Optional[OccupySize] = None,
    ) -> Result[ImageCopy]:
        # R-03 + AssetExists precondition
        if self._asset_repo.get_by_id(asset_id) is None:
            return Failure(NotFound(entity_kind="ImageAsset", entity_id=asset_id))

        # Validate optional initial values; map domain ValueError to UC failure.
        transform = initial_transform if initial_transform is not None else ImageTransform()
        if not isinstance(transform, ImageTransform):
            return Failure(
                InvalidTransform(
                    attempted_rotation=str(transform),
                    attempted_flip_x=None,
                    attempted_flip_y=None,
                )
            )

        scaling_mode = (
            initial_scaling_mode if initial_scaling_mode is not None else ScalingMode.UNIFORM_CONTAIN
        )
        if not isinstance(scaling_mode, ScalingMode):
            return Failure(InvalidScalingMode(attempted_value=str(scaling_mode)))

        alignment = initial_alignment if initial_alignment is not None else Alignment.MIDDLE_CENTER
        if not isinstance(alignment, Alignment):
            return Failure(InvalidAlignment(attempted_value=str(alignment)))

        if initial_occupy_size is None:
            occupy_size: OccupySize = OccupySize(width=1, height=1)
        else:
            try:
                if not isinstance(initial_occupy_size, OccupySize):
                    raise TypeError("OccupySize required")
                occupy_size = initial_occupy_size
            except (ValueError, TypeError):
                return Failure(
                    InvalidOccupySize(
                        attempted_width=getattr(initial_occupy_size, "width", None),
                        attempted_height=getattr(initial_occupy_size, "height", None),
                    )
                )

        # R-11 — copy_name empty string -> InvalidCopyName *via domain*.
        # But the prompt for UC-05 lists failure reasons NotFound /
        # InvalidAlignment / InvalidScalingMode / InvalidOccupySize / InvalidTransform.
        # Empty-string CopyName falls into "InvalidCopyName" by R-11 — UC-05's
        # failure_reasons list in the YAML does not include InvalidCopyName,
        # but R-11 is enforced at Domain. We reject with InvalidCopyName here
        # (documented in IMPLEMENTATION_NOTES.md as a candidate spec gap).
        if copy_name == "":
            return Failure(
                InvalidCopyName(detail="copy_name must be None or non-empty", attempted_value=copy_name)
            )

        now = self._clock()
        try:
            copy = ImageCopy(
                id=self._id_factory(),
                asset_id=asset_id,
                copy_name=copy_name,
                transform=transform,
                scaling_mode=scaling_mode,
                alignment=alignment,
                default_occupy_size=occupy_size,
                auto_crop=None,
                manual_crop=None,
                created_at=now,
                updated_at=now,
            )
        except (ValueError, TypeError) as e:
            # Last-resort guard — domain rejected. We don't expect to hit this
            # path under normal use because we pre-validated, but defensive.
            return Failure(InvalidTransform(attempted_rotation=str(e), attempted_flip_x=None, attempted_flip_y=None))

        self._copy_repo.save(copy)
        self._bus.publish(ImageCopyCreated(copy_id=copy.id, snapshot=copy))
        return Ok(copy)

    # ------------------------------------------------------------------
    # UC-06 DeleteImageCopy
    # ------------------------------------------------------------------

    def delete_image_copy(self, *, copy_id: str) -> Result[None]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        self._copy_repo.delete(copy_id)
        self._bus.publish(ImageCopyDeleted(copy_id=copy_id, snapshot_before=copy))
        return Ok(None)

    # ------------------------------------------------------------------
    # UC-07 ListImageCopies
    # ------------------------------------------------------------------

    def list_image_copies(self, *, asset_id: Optional[str] = None) -> Result[list[ImageCopy]]:
        return Ok(self._copy_repo.list_all(asset_id=asset_id))

    # ------------------------------------------------------------------
    # UC-08 GetImageCopy
    # ------------------------------------------------------------------

    def get_image_copy(self, *, copy_id: str) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        return Ok(copy)

    # ------------------------------------------------------------------
    # UC-09 ChangeCopyTransform
    # ------------------------------------------------------------------

    def change_copy_transform(self, *, copy_id: str, new_transform: ImageTransform) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_transform, ImageTransform):
            return Failure(
                InvalidTransform(
                    attempted_rotation=str(getattr(new_transform, "rotation", new_transform)),
                    attempted_flip_x=getattr(new_transform, "flip_x", None),
                    attempted_flip_y=getattr(new_transform, "flip_y", None),
                )
            )
        before = copy.transform
        if before == new_transform:
            return Ok(copy)  # no-op; do not emit event
        updated = copy.with_changes(now=self._clock(), transform=new_transform)
        self._copy_repo.save(updated)
        self._bus.publish(ImageCopyTransformChanged(copy_id=copy_id, before=before, after=new_transform))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-10 ChangeScalingMode
    # ------------------------------------------------------------------

    def change_scaling_mode(self, *, copy_id: str, new_scaling_mode: ScalingMode) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_scaling_mode, ScalingMode):
            return Failure(InvalidScalingMode(attempted_value=str(new_scaling_mode)))
        before = copy.scaling_mode
        if before == new_scaling_mode:
            return Ok(copy)
        updated = copy.with_changes(now=self._clock(), scaling_mode=new_scaling_mode)
        self._copy_repo.save(updated)
        self._bus.publish(
            ImageCopyScalingModeChanged(copy_id=copy_id, before=before, after=new_scaling_mode)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-11 ChangeAlignment
    # ------------------------------------------------------------------

    def change_alignment(self, *, copy_id: str, new_alignment: Alignment) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_alignment, Alignment):
            return Failure(InvalidAlignment(attempted_value=str(new_alignment)))
        before = copy.alignment
        if before == new_alignment:
            return Ok(copy)
        updated = copy.with_changes(now=self._clock(), alignment=new_alignment)
        self._copy_repo.save(updated)
        self._bus.publish(
            ImageCopyAlignmentChanged(copy_id=copy_id, before=before, after=new_alignment)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-12 ChangeAutoCropSettings
    # ------------------------------------------------------------------

    def change_auto_crop_settings(
        self,
        *,
        copy_id: str,
        target_color_argb: Optional[int],
        threshold: Optional[int],
    ) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))

        # R-06 aggregate check: both null OR both non-null.
        both_null = target_color_argb is None and threshold is None
        both_set = target_color_argb is not None and threshold is not None
        if not (both_null or both_set):
            return Failure(
                InvalidAutoCropSettings(
                    detail="target_color_argb and threshold must be both null or both non-null",
                    attempted_target_color=target_color_argb,
                    attempted_threshold=threshold,
                )
            )

        new_settings: Optional[AutoCropSettings]
        if both_null:
            new_settings = None
        else:
            try:
                new_settings = AutoCropSettings(
                    target_color_argb=int(target_color_argb),  # type: ignore[arg-type]
                    threshold=int(threshold),  # type: ignore[arg-type]
                )
            except (ValueError, TypeError) as e:
                return Failure(
                    InvalidAutoCropSettings(
                        detail=str(e),
                        attempted_target_color=target_color_argb,
                        attempted_threshold=threshold,
                    )
                )

        before = copy.auto_crop
        if before == new_settings:
            return Ok(copy)
        # R-08: We deliberately do NOT touch ``manual_crop`` here. AutoCrop and
        # ManualCrop coexist; the override interpretation is RENDERING_EXPORT's.
        updated = copy.with_changes(now=self._clock(), auto_crop=new_settings)
        self._copy_repo.save(updated)
        self._bus.publish(
            ImageCopyAutoCropChanged(copy_id=copy_id, before=before, after=new_settings)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-13 ChangeManualCropSettings
    # ------------------------------------------------------------------

    def change_manual_crop_settings(
        self,
        *,
        copy_id: str,
        x: Optional[float],
        y: Optional[float],
        width: Optional[float],
        height: Optional[float],
    ) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))

        # R-07 aggregate check: all-4-null OR all-4-non-null.
        values = (x, y, width, height)
        nones = sum(1 for v in values if v is None)
        if nones not in (0, 4):
            return Failure(
                InvalidManualCropFractions(
                    detail="all four (x, y, width, height) must be null together or all non-null",
                    x=x,
                    y=y,
                    width=width,
                    height=height,
                )
            )

        new_fraction: Optional[ManualCropFraction]
        if nones == 4:
            new_fraction = None
        else:
            try:
                new_fraction = ManualCropFraction(
                    x=float(x),  # type: ignore[arg-type]
                    y=float(y),  # type: ignore[arg-type]
                    width=float(width),  # type: ignore[arg-type]
                    height=float(height),  # type: ignore[arg-type]
                )
            except (ValueError, TypeError) as e:
                return Failure(
                    InvalidManualCropFractions(
                        detail=str(e), x=x, y=y, width=width, height=height
                    )
                )

        before = copy.manual_crop
        if before == new_fraction:
            return Ok(copy)
        # R-08: do NOT touch auto_crop.
        updated = copy.with_changes(now=self._clock(), manual_crop=new_fraction)
        self._copy_repo.save(updated)
        self._bus.publish(
            ImageCopyManualCropChanged(copy_id=copy_id, before=before, after=new_fraction)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-14 ChangeDefaultOccupySize
    # ------------------------------------------------------------------

    def change_default_occupy_size(
        self, *, copy_id: str, new_occupy_size: OccupySize
    ) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        if not isinstance(new_occupy_size, OccupySize):
            return Failure(
                InvalidOccupySize(
                    attempted_width=getattr(new_occupy_size, "width", None),
                    attempted_height=getattr(new_occupy_size, "height", None),
                )
            )
        before = copy.default_occupy_size
        if before == new_occupy_size:
            return Ok(copy)
        updated = copy.with_changes(now=self._clock(), default_occupy_size=new_occupy_size)
        self._copy_repo.save(updated)
        self._bus.publish(
            ImageCopyDefaultOccupySizeChanged(copy_id=copy_id, before=before, after=new_occupy_size)
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-15 RenameImageCopy
    # ------------------------------------------------------------------

    def rename_image_copy(self, *, copy_id: str, new_name: Optional[str]) -> Result[ImageCopy]:
        copy = self._copy_repo.get_by_id(copy_id)
        if copy is None:
            return Failure(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        # R-11: None OK, non-empty OK, empty string NOT OK.
        if new_name is not None:
            if not isinstance(new_name, str):
                return Failure(
                    InvalidCopyName(detail="new_name must be str or None", attempted_value=None)
                )
            if new_name == "":
                return Failure(
                    InvalidCopyName(
                        detail="empty string not allowed (use None for auto-generated name)",
                        attempted_value=new_name,
                    )
                )
        before = copy.copy_name
        if before == new_name:
            return Ok(copy)
        updated = copy.with_changes(now=self._clock(), copy_name=new_name)
        self._copy_repo.save(updated)
        self._bus.publish(ImageCopyRenamed(copy_id=copy_id, before=before, after=new_name))
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-16 ImageCopyExists  (cross-Capability query for GRID_COMPOSITION)
    # ------------------------------------------------------------------

    def image_copy_exists(self, *, copy_id: str) -> Result[bool]:
        return Ok(self._copy_repo.exists(copy_id))

    # ------------------------------------------------------------------
    # UC-17 ImageAssetExists
    # ------------------------------------------------------------------

    def image_asset_exists(self, *, asset_id: str) -> Result[bool]:
        return Ok(self._asset_repo.get_by_id(asset_id) is not None)
