"""Repository protocols + in-memory stub implementations.

Per 30-design.md §5.1, this Capability declares the protocols it CONSUMES
from the persistence boundary, plus a minimal `ImageCopyExistenceCheck`
interface for cross-capability dependency on IMAGE_VARIANT_MANAGEMENT.

CRITICAL — Decision Ownership (21-grid-composition.yaml §decision_ownership.
persistence_decision):
- Rule enforcement (R-01..R-09) MUST NOT be delegated to these repos. The
  in-memory stubs below are intentionally "dumb dicts" — no uniqueness
  constraints, no overlap checks. The use_case layer is the sole authority.
- We do NOT mark fields like placement_order as 'unique' here. R-06 lives
  in rules.orders_are_unique only.

UC-02 / UC-07 atomicity (design.md §5.2): the in-memory implementation
provides a `transaction()` context manager that performs copy-on-write
snapshotting so callers can roll back on validation failure. This is a
property of the *stub*, not of the Capability — a real RDBMS impl would
use SQL transactions, an event-source impl would write to a draft log, etc.
"""

from __future__ import annotations

from contextlib import contextmanager
from typing import Iterator, Protocol
from uuid import UUID

from .domain import GridCanvas, Placement


# -- Protocols --------------------------------------------------------------


class GridCanvasRepository(Protocol):
    def get_by_id(self, grid_id: UUID) -> GridCanvas | None: ...

    def save(self, grid_canvas: GridCanvas) -> None: ...

    def delete(self, grid_id: UUID) -> None: ...

    def list_all(self) -> list[GridCanvas]: ...


class PlacementRepository(Protocol):
    def get_by_id(self, placement_id: UUID) -> Placement | None: ...

    def get_by_grid(self, grid_id: UUID) -> list[Placement]: ...

    def save(self, placement: Placement) -> None: ...

    def delete(self, placement_id: UUID) -> None: ...


class ImageCopyExistenceCheck(Protocol):
    """The minimal cross-capability surface. Use cases ask "does this copy
    exist?" via this protocol; they NEVER ask for the ImageCopy itself, per
    20-capability-bom.md §4.2 ("CopyId の存在性のみ使う")."""

    def exists(self, copy_id: UUID) -> bool: ...


# -- In-memory implementations ---------------------------------------------


class InMemoryGridCanvasRepository:
    def __init__(self) -> None:
        self._store: dict[UUID, GridCanvas] = {}

    def get_by_id(self, grid_id: UUID) -> GridCanvas | None:
        return self._store.get(grid_id)

    def save(self, grid_canvas: GridCanvas) -> None:
        self._store[grid_canvas.id] = grid_canvas

    def delete(self, grid_id: UUID) -> None:
        self._store.pop(grid_id, None)

    def list_all(self) -> list[GridCanvas]:
        return list(self._store.values())

    # Transactional snapshot — used by UC-02 / UC-07 for atomicity per
    # 30-design.md §5.2. The stub provides this affordance so callers can
    # validate-then-commit. Production impls would use the underlying
    # storage engine's mechanism.
    @contextmanager
    def transaction(self) -> Iterator[None]:
        snapshot = dict(self._store)
        try:
            yield
        except Exception:
            self._store = snapshot
            raise


class InMemoryPlacementRepository:
    def __init__(self) -> None:
        self._store: dict[UUID, Placement] = {}

    def get_by_id(self, placement_id: UUID) -> Placement | None:
        return self._store.get(placement_id)

    def get_by_grid(self, grid_id: UUID) -> list[Placement]:
        return [p for p in self._store.values() if p.grid_id == grid_id]

    def save(self, placement: Placement) -> None:
        self._store[placement.id] = placement

    def delete(self, placement_id: UUID) -> None:
        self._store.pop(placement_id, None)

    @contextmanager
    def transaction(self) -> Iterator[None]:
        snapshot = dict(self._store)
        try:
            yield
        except Exception:
            self._store = snapshot
            raise


class InMemoryImageCopyExistenceCheck:
    """Trivial stub for cross-capability boundary. Test setup registers
    CopyIds it considers 'real'."""

    def __init__(self, known: set[UUID] | None = None) -> None:
        self._known: set[UUID] = set(known) if known else set()

    def exists(self, copy_id: UUID) -> bool:
        return copy_id in self._known

    # Test conveniences (not part of the protocol)
    def register(self, copy_id: UUID) -> None:
        self._known.add(copy_id)

    def forget(self, copy_id: UUID) -> None:
        self._known.discard(copy_id)
