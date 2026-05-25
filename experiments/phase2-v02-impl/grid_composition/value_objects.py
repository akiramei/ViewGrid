"""Value objects defined by the BOM (21-grid-composition.yaml §value_objects).

These objects carry only structural validity (non-negative ints etc.).
They do NOT decide grid-relative validity (R-01) — that is a UseCase-layer
concern per the Decision Ownership table.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class Axis(str, Enum):
    """Row vs. column. Used by UC-03, UC-04."""

    ROW = "Row"
    COL = "Col"


class OrderOperation(str, Enum):
    """UC-09 z-order operations (per 21-yaml::use_cases UC-09)."""

    BRING_TO_FRONT = "BringToFront"
    SEND_TO_BACK = "SendToBack"
    MOVE_FORWARD = "MoveForward"
    MOVE_BACKWARD = "MoveBackward"
    SET_ORDER = "SetOrder"


@dataclass(frozen=True)
class CellPosition:
    """Cell coordinate. ``x`` is column-wise, ``y`` is row-wise (graphics
    convention per 30-design §3.3)."""

    x: int
    y: int

    def __post_init__(self) -> None:
        # Structural validity only: cells are non-negative integers.
        # Grid-bound validity (R-01) is the UseCase layer's concern.
        if not isinstance(self.x, int) or isinstance(self.x, bool):
            raise TypeError("CellPosition.x must be int")
        if not isinstance(self.y, int) or isinstance(self.y, bool):
            raise TypeError("CellPosition.y must be int")
        if self.x < 0 or self.y < 0:
            raise ValueError(f"CellPosition must be non-negative: got ({self.x},{self.y})")


@dataclass(frozen=True)
class OccupySize:
    """Occupied cell count. Both dimensions must be >= 1 (30-design §3.3)."""

    width: int
    height: int

    def __post_init__(self) -> None:
        if not isinstance(self.width, int) or isinstance(self.width, bool):
            raise TypeError("OccupySize.width must be int")
        if not isinstance(self.height, int) or isinstance(self.height, bool):
            raise TypeError("OccupySize.height must be int")
        if self.width < 1 or self.height < 1:
            raise ValueError(
                f"OccupySize must be >= 1 in both dimensions: got ({self.width},{self.height})"
            )


@dataclass(frozen=True)
class PixelSize:
    """Output canvas size in pixels. Both dimensions > 0 (R-03)."""

    width: int
    height: int

    def __post_init__(self) -> None:
        if not isinstance(self.width, int) or isinstance(self.width, bool):
            raise TypeError("PixelSize.width must be int")
        if not isinstance(self.height, int) or isinstance(self.height, bool):
            raise TypeError("PixelSize.height must be int")
        if self.width <= 0 or self.height <= 0:
            raise ValueError(
                f"PixelSize must be positive in both dimensions: got ({self.width},{self.height})"
            )
