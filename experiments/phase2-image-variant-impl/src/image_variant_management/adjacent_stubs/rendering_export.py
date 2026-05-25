"""Stub for RENDERING_EXPORT (R-08 enforcement lives here, NOT in this Capability).

This stub exists purely to document where R-08 *would* be enforced. It is NOT
called by the IMAGE_VARIANT_MANAGEMENT UseCases; this Capability merely allows
``AutoCropSettings`` and ``ManualCropFraction`` to coexist on an ``ImageCopy``.
"""

from __future__ import annotations

from ..domain import AutoCropSettings, ImageCopy, ManualCropFraction


class RenderingExportStub:
    """Documents R-08 ownership without enforcing it."""

    @staticmethod
    def select_effective_crop(
        copy: ImageCopy,
    ) -> tuple[str, "AutoCropSettings | ManualCropFraction | None"]:
        """R-08 application — declared here for clarity, NOT used by IMAGE_VARIANT_MANAGEMENT.

        If we were the RENDERING_EXPORT Capability, this is where the
        ManualCrop-overrides-AutoCrop interpretation would live. We expose it
        only to document the contract; IMAGE_VARIANT_MANAGEMENT tests do not
        invoke this method (and the production code in this package never
        calls it).
        """

        if copy.manual_crop is not None:
            return ("manual", copy.manual_crop)
        if copy.auto_crop is not None:
            return ("auto", copy.auto_crop)
        return ("none", None)
