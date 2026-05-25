"""IMAGE_VARIANT_MANAGEMENT — stub boundary only.

GRID_COMPOSITION depends on this capability for *existence checks* on
CopyId values (see 20-capability-bom.md §8.1). It does NOT consume the
ImageCopy entity itself.

This stub declares the single interface GRID_COMPOSITION needs. The actual
implementation (creating, editing, deriving ImageCopy values) is OUT OF
SCOPE for this experiment.
"""

from __future__ import annotations

from typing import Protocol
from uuid import UUID


class ImageCopyExistenceProvider(Protocol):
    """The single method GRID_COMPOSITION can call on this capability."""

    def exists(self, copy_id: UUID) -> bool: ...


# NOTE: A real IMAGE_VARIANT_MANAGEMENT would expose CreateImageCopy,
# EditImageCopyTransform, etc. NONE of those are visible to
# GRID_COMPOSITION (see 20-capability-bom.md §4.2 — "本 Capability は
# CopyId の存在性のみ使う").
