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

  - C-CONSUMER-PORTS (v0.2 / n=3): consumer read ports GridLayoutPort and
    CopyRenderSpecPort defined ONCE here as Protocols. They return *neutral* DTOs
    from src/shared/render_contracts.py (never producer domain types). Not-found
    is expressed as None (C-REPO-NOTFOUND), never wrapped in Result.
      * GRID side:  GridCompositionUseCases satisfies GridLayoutPort natively
        (get_grid_layout projects GridCanvas + placements -> GridLayout).
      * IMGVAR side: ImageVariantManagementUseCases satisfies CopyRenderSpecPort
        natively (get_copy_render_spec projects ImageCopy -> CopyRenderSpec).
      * RENDERING side: depends only on these ports + neutral DTOs (no adapter).
"""

from __future__ import annotations

import uuid
from typing import Protocol, runtime_checkable

from shared.render_contracts import CopyRenderSpec, GridLayout


@runtime_checkable
class ImageCopyExistencePort(Protocol):
    def exists(self, copy_id: uuid.UUID) -> bool: ...


@runtime_checkable
class GridLayoutPort(Protocol):
    """Consumer read port: GRID geometry + placements as a neutral GridLayout."""

    def get_grid_layout(self, grid_id: uuid.UUID) -> GridLayout | None: ...


@runtime_checkable
class CopyRenderSpecPort(Protocol):
    """Consumer read port: an ImageCopy's render settings as a neutral DTO."""

    def get_copy_render_spec(self, copy_id: uuid.UUID) -> CopyRenderSpec | None: ...
