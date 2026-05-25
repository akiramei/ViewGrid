"""Stub for GRID_COMPOSITION as seen from IMAGE_VARIANT_MANAGEMENT.

Per ``20-capability-bom.md`` §8.1, the only contractual contact point between
the two Capabilities (from this side) is:

* GRID_COMPOSITION calls our ``UC-16 ImageCopyExists``.
* GRID_COMPOSITION subscribes to our ``ImageCopyDeleted`` event.

This stub demonstrates both contracts — it can subscribe to an EventBus and
remember which copy_ids it has "seen deleted". It exists purely to validate
that *our* side of the contract is callable; no Placement logic lives here.
"""

from __future__ import annotations

from typing import Callable

from ..events import EventBus, ImageCopyDeleted


class GridCompositionStub:
    def __init__(
        self,
        *,
        image_copy_exists: Callable[[str], bool],
        event_bus: EventBus,
    ) -> None:
        self._image_copy_exists = image_copy_exists
        self._deleted_copy_ids: list[str] = []
        event_bus.subscribe(self._on_event)

    def _on_event(self, event: object) -> None:
        if isinstance(event, ImageCopyDeleted):
            self._deleted_copy_ids.append(event.copy_id)

    @property
    def observed_deleted_copy_ids(self) -> list[str]:
        return list(self._deleted_copy_ids)

    def check_image_copy_exists(self, copy_id: str) -> bool:
        """Mirror what GRID_COMPOSITION.UC-05 would do as a precondition."""

        return self._image_copy_exists(copy_id)
