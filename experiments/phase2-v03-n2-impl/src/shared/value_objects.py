"""Shared value objects (C-SHARED-PLACEMENT / C-VALUE-SEMANTICS).

OccupySize / PixelSize are defined here ONCE and imported by every Capability.
Local redefinition is forbidden (Addendum E collision #2).
"""
from __future__ import annotations

from dataclasses import dataclass


def _reject_bool_as_int(value: object, field: str) -> None:
    # C-VALUE-SEMANTICS: bool must be rejected as int (OccupySize(True, 1) -> TypeError).
    if isinstance(value, bool):
        raise TypeError(f"{field} must be an int, not bool")
    if not isinstance(value, int):
        raise TypeError(f"{field} must be an int")


@dataclass(frozen=True)
class OccupySize:
    """Occupy cell count (width = columns, height = rows). Both >= 1."""

    width: int
    height: int

    def __post_init__(self) -> None:
        _reject_bool_as_int(self.width, "width")
        _reject_bool_as_int(self.height, "height")
        if self.width < 1:
            raise ValueError("OccupySize.width must be >= 1")
        if self.height < 1:
            raise ValueError("OccupySize.height must be >= 1")


@dataclass(frozen=True)
class PixelSize:
    """Output size in pixels. Both > 0."""

    width: int
    height: int

    def __post_init__(self) -> None:
        _reject_bool_as_int(self.width, "width")
        _reject_bool_as_int(self.height, "height")
        if self.width < 1:
            raise ValueError("PixelSize.width must be > 0")
        if self.height < 1:
            raise ValueError("PixelSize.height must be > 0")
