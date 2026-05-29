"""Cross-Capability boundary ports (C-BOUNDARY-IFACE + C-CONSUMER-PORTS).

Defined ONCE here so both sides share the same Protocol -> zero adapters.

- ImageCopyExistencePort : existing n=2 boundary (bool existence check).
- GridLayoutPort / CopyRenderSpecPort : v0.3 read ports, PRE-LOADED at n=2
  generation time even though the consumer (RENDERING) does not exist yet.
"""
from __future__ import annotations

import uuid
from typing import Protocol

from .render_contracts import CopyRenderSpec, GridLayout


class ImageCopyExistencePort(Protocol):
    """n=2 existing boundary. Returns a plain bool, not Result (C-BOUNDARY-IFACE)."""

    def exists(self, copy_id: uuid.UUID) -> bool: ...


class GridLayoutPort(Protocol):
    """Read port for GRID's neutral layout projection (pre-loaded, v0.3)."""

    def get_grid_layout(self, grid_id: uuid.UUID) -> GridLayout | None: ...


class CopyRenderSpecPort(Protocol):
    """Read port for IMGVAR's neutral copy spec projection (pre-loaded, v0.3)."""

    def get_copy_render_spec(self, copy_id: uuid.UUID) -> CopyRenderSpec | None: ...
