"""Canonical failure-reason types.

Per 20-capability-bom.md §2 and 40-ai-implementation-prompt.md FORBIDDEN,
these names MUST NOT be renamed or added to. They are reused by tests and by
upstream callers as a contract.

Each error carries optional structured data per design 30-design.md §1 R-02
("Conflict should include the conflicting placement_id").
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Sequence
from uuid import UUID


class UseCaseError(Exception):
    """Base class. Use cases raise subclasses; callers may catch this one."""


@dataclass(frozen=True)
class InvalidDimensions(UseCaseError):
    """Grid rows/cols/canvas_size not strictly positive. UC-01, UC-02."""

    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover - trivial
        return self.detail or "InvalidDimensions"


@dataclass(frozen=True)
class InvalidWeights(UseCaseError):
    """Weight array length mismatch or non-positive element. UC-03."""

    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or "InvalidWeights"


@dataclass(frozen=True)
class InvalidIndex(UseCaseError):
    """Index out of range for axis lock. UC-04."""

    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or "InvalidIndex"


@dataclass(frozen=True)
class InvalidOrderValue(UseCaseError):
    """Invalid order operation / value. UC-09."""

    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or "InvalidOrderValue"


@dataclass(frozen=True)
class OutOfBounds(UseCaseError):
    """Placement rectangle would extend past grid boundary. R-01."""

    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or "OutOfBounds"


@dataclass(frozen=True)
class Conflict(UseCaseError):
    """Placement rectangle overlaps another placement's. R-02.

    `conflicting_placement_ids` carries the offending placement ids per design
    30-design.md §1 R-02 ("衝突相手の placement_id を結果に含めること").
    """

    conflicting_placement_ids: Sequence[UUID] = field(default_factory=tuple)
    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or f"Conflict(with={list(self.conflicting_placement_ids)})"


@dataclass(frozen=True)
class UnknownCopyId(UseCaseError):
    """CopyId not present in ImageCopyExistenceCheck. UC-05."""

    copy_id: UUID | None = None
    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or f"UnknownCopyId({self.copy_id})"


@dataclass(frozen=True)
class WouldOrphanPlacements(UseCaseError):
    """UC-02: new dimensions would push existing placement(s) out of bounds."""

    orphaned_placement_ids: Sequence[UUID] = field(default_factory=tuple)
    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or f"WouldOrphanPlacements({list(self.orphaned_placement_ids)})"


@dataclass(frozen=True)
class WouldConflict(UseCaseError):
    """UC-02: new dimensions would cause placements to overlap (defensive)."""

    conflicting_placement_ids: Sequence[UUID] = field(default_factory=tuple)
    detail: str = ""

    def __str__(self) -> str:  # pragma: no cover
        return self.detail or f"WouldConflict({list(self.conflicting_placement_ids)})"
