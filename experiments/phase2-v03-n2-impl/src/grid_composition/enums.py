"""GRID_COMPOSITION enums (C-ENUM: enum.Enum)."""
from __future__ import annotations

import enum


class Axis(enum.Enum):
    Row = "Row"
    Col = "Col"


class OrderOperation(enum.Enum):
    BringToFront = "BringToFront"
    SendToBack = "SendToBack"
    MoveForward = "MoveForward"
    MoveBackward = "MoveBackward"
    SetOrder = "SetOrder"
