"""IMAGE_VARIANT_MANAGEMENT Capability.

Authority over ImageAsset / ImageCopy: settings validity, hash dedup, existence.
Does NOT own cascade decisions (UC-02 refuses with DependentCopiesExist) and
does NOT apply R-08 (ManualCropOverridesAutoCrop) -- declaration only.
"""
