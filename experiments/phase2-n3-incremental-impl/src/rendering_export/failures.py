"""RENDERING_EXPORT canonical failure reasons (20/21 §canonical_failure_reasons).

Only NotFound exists for this focused Capability. It carries entity_kind
("Grid" | "ImageCopy") + entity_id (identity = uuid.UUID, C-IDENTITY). Wrapped
in Err(...) (C-RESULT); never raised.
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass


@dataclass(frozen=True)
class NotFound:
    entity_kind: str          # "Grid" | "ImageCopy"
    entity_id: uuid.UUID
