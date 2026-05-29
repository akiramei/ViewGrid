"""Neutral cross-Capability read DTOs (C-CONSUMER-PORTS, contract v0.3 §1.8).

These DTOs carry NO producer enums / domain types. Enums are represented as
neutral str. They are pre-loaded (built into n=2) even though no consumer
(RENDERING) exists yet -- this is the v0.3 "up-front" mandate.
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass


@dataclass(frozen=True)
class PlacementView:
    """Neutral projection of a GRID Placement."""

    copy_id: uuid.UUID
    x: int
    y: int
    occupy_w: int
    occupy_h: int
    order: int  # placement_order (z order)


@dataclass(frozen=True)
class GridLayout:
    """Neutral projection of GridCanvas + its placements."""

    grid_rows: int
    grid_cols: int
    col_weights: tuple[int, ...]
    row_weights: tuple[int, ...]
    canvas_w: int
    canvas_h: int
    placements: tuple[PlacementView, ...]


@dataclass(frozen=True)
class CopyRenderSpec:
    """Neutral projection of an ImageCopy (R-08 NOT applied here -- RENDERING's job)."""

    rotation: str  # "None" | "CW90" | "CW180" | "CW270"
    flip_x: bool
    flip_y: bool
    scaling_mode: str  # "UniformContain" | "UniformCover" | "Fill"
    alignment: str  # 9 anchor names
    auto_crop: tuple[int, int] | None  # (target_color_argb, threshold) or None
    manual_crop: tuple[float, float, float, float] | None  # (x, y, w, h) or None
