"""Synchronous in-process event bus shared by both Capabilities.

Contract binding (00-convention-contract.md §2):
  - C-EVENTBUS: synchronous in-process; a RecordingBus is shared for tests.

The bus is intentionally tiny: publish() dispatches synchronously to any
subscribers and records every event for test inspection (event emission must be
observable independently of state change — see both 30-design.md §4).
"""

from __future__ import annotations

from typing import Any, Callable


class RecordingBus:
    """Synchronous bus that records every published event.

    Both Capabilities publish through one instance so cross-Capability event
    flows (e.g. ImageCopyDeleted observed by GRID) are observable in one place.
    """

    def __init__(self) -> None:
        self.events: list[Any] = []
        self._subscribers: list[Callable[[Any], None]] = []

    def subscribe(self, handler: Callable[[Any], None]) -> None:
        self._subscribers.append(handler)

    def publish(self, event: Any) -> None:
        self.events.append(event)
        for handler in list(self._subscribers):
            handler(event)

    # --- test helpers -----------------------------------------------------
    def of_type(self, event_type: type) -> list[Any]:
        return [e for e in self.events if isinstance(e, event_type)]

    def clear(self) -> None:
        self.events.clear()
