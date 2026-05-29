"""RENDERING_EXPORT canonical failure reasons (21-rendering-export.yaml)."""
from __future__ import annotations

import uuid
from dataclasses import dataclass


@dataclass(frozen=True)
class NotFound:
    entity_kind: str  # "Grid" | "ImageCopy"
    entity_id: uuid.UUID
