"""RENDERING_EXPORT events (21-rendering-export.yaml §events).

Emitted only on UseCase success, through the shared synchronous RecordingBus
(C-EVENTBUS). Names are canonical.
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass


@dataclass(frozen=True)
class RenderModelBuilt:
    grid_id: uuid.UUID
    item_count: int


@dataclass(frozen=True)
class RenderDescriptorExported:
    grid_id: uuid.UUID
    item_count: int
