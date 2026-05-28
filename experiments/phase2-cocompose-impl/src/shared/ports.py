"""Cross-Capability boundary ports.

Contract binding (00-convention-contract.md):
  - C-BOUNDARY-IFACE: ImageCopyExistencePort defined ONCE here as a Protocol,
    shared by both sides.
      * GRID side: UC-05 (PlaceImageCopy) depends on this Port; exists()==False
        -> UnknownCopyId.
      * IMAGE_VARIANT side: ImageVariantManagementUseCases SATISFIES this Port
        directly (exposes exists(copy_id) running UC-16 internally), so it can
        be passed into GRID with NO adapter.

  - exists() returns a plain bool (NOT wrapped in Result).
  - argument is uuid.UUID (C-IDENTITY).
"""

from __future__ import annotations

import uuid
from typing import Protocol, runtime_checkable


@runtime_checkable
class ImageCopyExistencePort(Protocol):
    def exists(self, copy_id: uuid.UUID) -> bool: ...
