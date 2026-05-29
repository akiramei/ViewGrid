"""RENDERING_EXPORT local domain types.

These are RENDERING-local output types. RENDERING depends ONLY on shared
neutral DTOs (GridLayout / PlacementView / CopyRenderSpec) -- it does NOT
import grid_composition / image_variant_management domain types
(C-CONSUMER-PORTS).

C-IDENTITY-BOUNDARY (contract v0.3 §1.9):
  - internal representation keeps identities as uuid.UUID
  - the *output boundary* (RenderDescriptor / to_dict) str()-ifies identities
    so json.dumps works.
"""
from __future__ import annotations

import uuid
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class EffectiveCrop:
    """R-02 result: 'manual' | 'auto' | 'none'."""

    kind: str
    value: Any  # tuple (manual bbox) | tuple (auto) | None


@dataclass(frozen=True)
class RenderItem:
    copy_id: uuid.UUID  # internal: UUID (C-IDENTITY)
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

    def to_dict(self) -> dict[str, Any]:
        # C-IDENTITY-BOUNDARY: identity str()-ified at the output boundary.
        return {
            "copy_id": str(self.copy_id),
            "px": self.px,
            "py": self.py,
            "pw": self.pw,
            "ph": self.ph,
            "effective_crop": {"kind": self.effective_crop.kind, "value": self.effective_crop.value},
            "scaling_mode": self.scaling_mode,
            "alignment": self.alignment,
            "rotation": self.rotation,
            "flip_x": self.flip_x,
            "flip_y": self.flip_y,
        }


@dataclass(frozen=True)
class RenderModel:
    grid_id: uuid.UUID  # internal: UUID
    canvas_w: int
    canvas_h: int
    items: tuple[RenderItem, ...]  # z-order ascending (R-01)


@dataclass(frozen=True)
class RenderDescriptor:
    """Serializable form of a RenderModel (C-IDENTITY-BOUNDARY: identities are str)."""

    grid_id: str  # str at the output boundary
    canvas_w: int
    canvas_h: int
    items: tuple[dict[str, Any], ...]

    @staticmethod
    def from_model(model: RenderModel) -> "RenderDescriptor":
        return RenderDescriptor(
            grid_id=str(model.grid_id),
            canvas_w=model.canvas_w,
            canvas_h=model.canvas_h,
            items=tuple(item.to_dict() for item in model.items),
        )

    def to_dict(self) -> dict[str, Any]:
        return {
            "grid_id": self.grid_id,
            "canvas_w": self.canvas_w,
            "canvas_h": self.canvas_h,
            "items": list(self.items),
        }
