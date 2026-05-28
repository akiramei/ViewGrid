"""Shared *neutral* DTOs for the consumer read boundary (C-CONSUMER-PORTS, v0.2).

Contract bindings (00-convention-contract.md §1.8 C-CONSUMER-PORTS):
  - These DTOs live ONCE in src/shared and are the ONLY shapes that cross the
    GRID/IMGVAR -> RENDERING_EXPORT boundary.
  - They carry NO producer domain types and NO producer enums. Enum-valued
    fields (rotation / scaling_mode / alignment) are plain `str` (producer maps
    via `.value` when satisfying the port). This is what keeps the consumer from
    importing grid_composition.Placement or image_variant_management.ImageCopy.
  - CopyRenderSpec deliberately leaves R-08 (ManualCropOverridesAutoCrop)
    *unapplied*: it exposes BOTH auto_crop and manual_crop. The application of
    R-08 is the consumer's (RENDERING UC-02) responsibility.

Identity stays uuid.UUID (C-IDENTITY); no str conversion.
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass


@dataclass(frozen=True)
class PlacementView:
    """Neutral projection of a GRID Placement (no GRID domain types)."""

    copy_id: uuid.UUID
    x: int          # cell column (0-based)
    y: int          # cell row (0-based)
    occupy_w: int   # occupied columns
    occupy_h: int   # occupied rows
    order: int      # placement_order (z order)


@dataclass(frozen=True)
class GridLayout:
    """Neutral projection of a GridCanvas + its placements."""

    grid_rows: int
    grid_cols: int
    col_weights: tuple[int, ...]
    row_weights: tuple[int, ...]
    canvas_w: int
    canvas_h: int
    placements: tuple[PlacementView, ...]


@dataclass(frozen=True)
class CopyRenderSpec:
    """Neutral projection of an ImageCopy's render settings.

    R-08 is *not* applied here (auto_crop and manual_crop may both be present);
    the consumer (RENDERING UC-02) resolves the effective crop.
    """

    rotation: str            # "None" | "CW90" | "CW180" | "CW270"
    flip_x: bool
    flip_y: bool
    scaling_mode: str        # "UniformContain" | "UniformCover" | "Fill"
    alignment: str           # one of 9 anchor names
    auto_crop: tuple[int, int] | None                       # (target_color_argb, threshold) or None
    manual_crop: tuple[float, float, float, float] | None   # (x, y, w, h) or None
