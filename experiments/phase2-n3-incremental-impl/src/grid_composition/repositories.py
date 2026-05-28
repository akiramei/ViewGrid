"""In-memory repository stubs for GRID_COMPOSITION.

Persistence is out of scope (30-design.md §5); these stubs let the UseCase layer
own all Rule enforcement. C-REPO-NOTFOUND: "not found" returns None (no raise).
Repositories never enforce Rules (R-06 uniqueness etc. are the UseCase's job).
"""

from __future__ import annotations

import uuid

from grid_composition.domain import GridCanvas, Placement


class InMemoryGridCanvasRepository:
    def __init__(self) -> None:
        self._grids: dict[uuid.UUID, GridCanvas] = {}

    def get_by_id(self, grid_id: uuid.UUID) -> GridCanvas | None:
        return self._grids.get(grid_id)

    def save(self, grid: GridCanvas) -> None:
        self._grids[grid.id] = grid

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
