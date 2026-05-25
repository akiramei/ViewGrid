"""Repository interfaces and in-memory implementations.

The repositories MUST NOT enforce any Rule (R-01..R-09). They are pure
storage. In-memory implementations are provided for tests and for the
sample's `existence_check_only` ImageCopy stub.

MUST_DECIDE_AND_DOCUMENT:
    "not found" is signalled by ``None`` (Optional return). We do NOT
    raise here — the UseCase layer turns ``None`` into a ``NotFound``
    failure value.
"""

from __future__ import annotations

from typing import Dict, List, Optional, Protocol, Set

from .entities import GridCanvas, Placement
from .identity import Id


class GridCanvasRepository(Protocol):
    def get_by_id(self, grid_id: Id) -> Optional[GridCanvas]: ...
    def save(self, grid_canvas: GridCanvas) -> None: ...
    def delete(self, grid_id: Id) -> None: ...
    def list_all(self) -> List[GridCanvas]: ...


class PlacementRepository(Protocol):
    def get_by_id(self, placement_id: Id) -> Optional[Placement]: ...
    def get_by_grid(self, grid_id: Id) -> List[Placement]: ...
    def save(self, placement: Placement) -> None: ...
    def delete(self, placement_id: Id) -> None: ...


class ImageCopyExistenceCheck(Protocol):
    """Stub for IMAGE_VARIANT_MANAGEMENT capability.

    Per 20-capability-bom §8.1 and 21-yaml §entities.referenced.ImageCopy,
    GRID_COMPOSITION may only query existence. It must NOT read any
    other field (no interpretation of Scaling, Crop, Transform).
    """

    def exists(self, copy_id: Id) -> bool: ...


# --------------------------------------------------------------------------
# In-memory implementations (used by tests and the demo CLI if any)
# --------------------------------------------------------------------------


class InMemoryGridCanvasRepository:
    def __init__(self) -> None:
        self._store: Dict[Id, GridCanvas] = {}

    def get_by_id(self, grid_id: Id) -> Optional[GridCanvas]:
        return self._store.get(grid_id)

    def save(self, grid_canvas: GridCanvas) -> None:
        self._store[grid_canvas.id] = grid_canvas

    def delete(self, grid_id: Id) -> None:
        self._store.pop(grid_id, None)

    def list_all(self) -> List[GridCanvas]:
        return list(self._store.values())


class InMemoryPlacementRepository:
    def __init__(self) -> None:
        self._store: Dict[Id, Placement] = {}

    def get_by_id(self, placement_id: Id) -> Optional[Placement]:
        return self._store.get(placement_id)

    def get_by_grid(self, grid_id: Id) -> List[Placement]:
        return [p for p in self._store.values() if p.grid_id == grid_id]

    def save(self, placement: Placement) -> None:
        self._store[placement.id] = placement

    def delete(self, placement_id: Id) -> None:
        self._store.pop(placement_id, None)


class InMemoryImageCopyExistenceCheck:
    """Test double for IMAGE_VARIANT_MANAGEMENT existence."""

    def __init__(self, known: Optional[Set[Id]] = None) -> None:
        self._known: Set[Id] = set(known) if known else set()

    def add(self, copy_id: Id) -> None:
        self._known.add(copy_id)

    def remove(self, copy_id: Id) -> None:
        self._known.discard(copy_id)

    def exists(self, copy_id: Id) -> bool:
        return copy_id in self._known
