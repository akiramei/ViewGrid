"""In-memory repositories for GRID_COMPOSITION.

Persistence form is AI-free; Rules are NOT delegated to the repository
(no DB unique constraints standing in for R-06 etc.). not-found -> None
(C-REPO-NOTFOUND).
"""
from __future__ import annotations

import uuid

from .domain import GridCanvas, Placement


class InMemoryGridCanvasRepository:
    def __init__(self) -> None:
        self._grids: dict[uuid.UUID, GridCanvas] = {}

    def get_by_id(self, grid_id: uuid.UUID) -> GridCanvas | None:
        return self._grids.get(grid_id)

    def save(self, grid_canvas: GridCanvas) -> None:
        self._grids[grid_canvas.id] = grid_canvas

    def delete(self, grid_id: uuid.UUID) -> None:
        self._grids.pop(grid_id, None)

    def list_all(self) -> list[GridCanvas]:
        return list(self._grids.values())


class InMemoryPlacementRepository:
    def __init__(self) -> None:
        self._placements: dict[uuid.UUID, Placement] = {}

    def get_by_id(self, placement_id: uuid.UUID) -> Placement | None:
        return self._placements.get(placement_id)

    def get_by_grid(self, grid_id: uuid.UUID) -> list[Placement]:
        return [p for p in self._placements.values() if p.grid_id == grid_id]

    def save(self, placement: Placement) -> None:
        self._placements[placement.id] = placement

    def delete(self, placement_id: uuid.UUID) -> None:
        self._placements.pop(placement_id, None)
