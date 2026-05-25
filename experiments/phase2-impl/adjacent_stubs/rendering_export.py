"""RENDERING_EXPORT — stub boundary only.

GRID_COMPOSITION provides logical placement data (position, occupy_size,
order, plus the grid's canvas_size as a pure value). RENDERING_EXPORT
reads that data and produces pixels (PNG). All rendering decisions live
THERE, not here (see 20-capability-bom.md §8.3).

This stub shows the read-only interface RENDERING would consume.
"""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from grid_composition.domain import GridCanvas, Placement


class CompositionReader(Protocol):
    """Read-only view that RENDERING_EXPORT would call into."""

    def get_canvas(self, grid_id: UUID) -> GridCanvas | None: ...

    def list_placements_in_z_order(self, grid_id: UUID) -> list[Placement]: ...


# NOTE: RENDERING_EXPORT must NOT modify the GridCanvas or its Placements.
# It also must NOT enforce R-01..R-09 — those are owned by GRID_COMPOSITION.
