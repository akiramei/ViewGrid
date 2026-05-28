"""RENDERING_EXPORT local domain model (output types).

These are RENDERING-local types (20-capability-bom.md §4). Inputs arrive only as
shared neutral DTOs (GridLayout / PlacementView / CopyRenderSpec); RENDERING does
NOT import GRID's Placement or IMGVAR's ImageCopy (C-CONSUMER-PORTS).

Crop is carried opaquely through EffectiveCrop.value (the consumer never
re-validates crop values — that is IMGVAR's authority, R-06/R-07).

MUST_DECIDE_AND_DOCUMENT (RENDERING-local, see IMPLEMENTATION_NOTES_N3.md):
  - EffectiveCrop representation type: a dataclass with kind:str + value:Any.
  - RenderDescriptor dict schema: see RenderModel.to_descriptor / RenderItem.
"""

from __future__ import annotations

import enum
import uuid
from dataclasses import dataclass
from typing import Any


class CropKind(enum.Enum):
    # C-ENUM: enum.Enum. The 3-valued result of R-02 application.
    Manual = "manual"
    Auto = "auto"
    NoneKind = "none"


@dataclass(frozen=True)
class EffectiveCrop:
    """R-08 application result (R-02 in RENDERING's ledger).

    kind is one of "manual" / "auto" / "none". value carries the raw crop tuple
    from the neutral CopyRenderSpec (manual: (x,y,w,h); auto: (argb, threshold);
    none: None). RENDERING never re-validates these values.
    """

    kind: str          # "manual" | "auto" | "none" (== CropKind.value)
    value: Any         # tuple for manual/auto, None for none

    def to_dict(self) -> dict:
        return {"kind": self.kind, "value": self.value}


@dataclass(frozen=True)
class RenderItem:
    """One drawable, projected from a PlacementView + its CopyRenderSpec."""

    copy_id: uuid.UUID
    px: int
    py: int
    pw: int
    ph: int
    effective_crop: EffectiveCrop
    scaling_mode: str
    alignment: str
    rotation: str
    flip_x: bool
    flip_y: bool

    def to_dict(self) -> dict:
        return {
            "copy_id": self.copy_id,
            "px": self.px,
            "py": self.py,
            "pw": self.pw,
            "ph": self.ph,
            "effective_crop": self.effective_crop.to_dict(),
            "scaling_mode": self.scaling_mode,
            "alignment": self.alignment,
            "rotation": self.rotation,
            "flip_x": self.flip_x,
            "flip_y": self.flip_y,
        }


@dataclass(frozen=True)
class RenderModel:
    """A grid's z-ordered drawable list + canvas info."""

    grid_id: uuid.UUID
    canvas_w: int
    canvas_h: int
    items: tuple[RenderItem, ...]   # already in placement_order (z) order

    def to_descriptor(self) -> dict:
        """Serializable dict form (RenderDescriptor). Deterministic."""
        return {
            "grid_id": self.grid_id,
            "canvas_w": self.canvas_w,
            "canvas_h": self.canvas_h,
            "items": [item.to_dict() for item in self.items],
        }
