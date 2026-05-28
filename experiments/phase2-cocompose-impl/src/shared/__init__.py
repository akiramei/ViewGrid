"""Shared kernel for GRID_COMPOSITION and IMAGE_VARIANT_MANAGEMENT.

This package holds the *single* physical definitions that the convention
contract (docs/capability-bom-sample/00-convention-contract.md) mandates be
shared by both Capabilities:

- value_objects.OccupySize / PixelSize   (C-SHARED-PLACEMENT / C-VALUE-SEMANTICS)
- result.Ok / result.Err                  (C-RESULT)
- ports.ImageCopyExistencePort            (C-BOUNDARY-IFACE)
"""
