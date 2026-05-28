"""Shared value objects.

Contract bindings (00-convention-contract.md):
  - C-SHARED-PLACEMENT: OccupySize / PixelSize defined ONCE here, imported by both.
  - C-VALUE-SEMANTICS:   frozen dataclass; reject bool-as-int; values >= 1.

These are the *same* Python types in both Capabilities (identity by import).
"""

from __future__ import annotations

from dataclasses import dataclass


def _check_positive_int(name: str, value: object) -> int:
    # C-VALUE-SEMANTICS: bool must be rejected even though bool is a subclass of
    # int in Python.  OccupySize(True, 1) must raise TypeError.
    if isinstance(value, bool):
        raise TypeError(f"{name} must be an int, not bool (got {value!r})")
    if not isinstance(value, int):
        raise TypeError(f"{name} must be an int (got {type(value).__name__})")
    if value < 1:
        raise ValueError(f"{name} must be >= 1 (got {value})")
    return value


@dataclass(frozen=True)
class OccupySize:
    """Occupied cell count (width = columns, height = rows). Both >= 1."""

    width: int
    height: int

    def __post_init__(self) -> None:
        _check_positive_int("OccupySize.width", self.width)
        _check_positive_int("OccupySize.height", self.height)


@dataclass(frozen=True)
class PixelSize:
    """Output / pixel size (width, height). Both >= 1 (i.e. > 0)."""

    width: int
    height: int

    def __post_init__(self) -> None:
        _check_positive_int("PixelSize.width", self.width)
        _check_positive_int("PixelSize.height", self.height)
