"""IMAGE_VARIANT_MANAGEMENT Capability (Phase 2 v0.1 sample implementation)."""

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
from .image_decoder import ImageDecoder, MockImageDecoder, PillowImageDecoder
from .repositories import (
    ImageAssetRepository,
    ImageBlobStorage,
    ImageCopyRepository,
    InMemoryImageAssetRepository,
    InMemoryImageBlobStorage,
    InMemoryImageCopyRepository,
)
from .shared import OccupySize, PixelSize
from .use_cases import DEFAULT_SUPPORTED_MIME_TYPES, ImageVariantManagementService

__all__ = [
    # domain
    "Alignment",
    "AutoCropSettings",
    "ImageAsset",
    "ImageCopy",
    "ImageTransform",
    "ManualCropFraction",
    "Rotation",
    "ScalingMode",
    "SourceType",
    "utc_now",
    # value objects (shared)
    "OccupySize",
    "PixelSize",
    # events
    "EventBus",
    "ImageAssetDeleted",
    "ImageAssetImported",
    "ImageAssetImportedAsDuplicate",
    "ImageCopyAlignmentChanged",
    "ImageCopyAutoCropChanged",
    "ImageCopyCreated",
    "ImageCopyDefaultOccupySizeChanged",
    "ImageCopyDeleted",
    "ImageCopyManualCropChanged",
    "ImageCopyRenamed",
    "ImageCopyScalingModeChanged",
    "ImageCopyTransformChanged",
    # failures
    "DependentCopiesExist",
    "Failure",
    "InvalidAlignment",
    "InvalidAutoCropSettings",
    "InvalidCopyName",
    "InvalidImageData",
    "InvalidManualCropFractions",
    "InvalidOccupySize",
    "InvalidScalingMode",
    "InvalidTransform",
    "NotFound",
    "Ok",
    "Result",
    "UnsupportedMimeType",
    # decoder
    "ImageDecoder",
    "MockImageDecoder",
    "PillowImageDecoder",
    # repositories
    "ImageAssetRepository",
    "ImageBlobStorage",
    "ImageCopyRepository",
    "InMemoryImageAssetRepository",
    "InMemoryImageBlobStorage",
    "InMemoryImageCopyRepository",
    # use cases
    "DEFAULT_SUPPORTED_MIME_TYPES",
    "ImageVariantManagementService",
]
