"""Shared synchronous in-process EventBus (C-EVENTBUS).

A RecordingBus is provided for tests so event emission can be observed
independently of state change.
"""
from __future__ import annotations

from typing import Callable, Protocol


class EventBus(Protocol):
    def publish(self, event: object) -> None: ...


class RecordingBus:
    """Synchronous in-process bus that records every published event."""

    def __init__(self) -> None:
        self.events: list[object] = []
        self._subscribers: list[Callable[[object], None]] = []

    def publish(self, event: object) -> None:
        self.events.append(event)
        for sub in list(self._subscribers):
            sub(event)

    def subscribe(self, handler: Callable[[object], None]) -> None:
        self._subscribers.append(handler)

    def of_type(self, event_type: type) -> list[object]:
        return [e for e in self.events if isinstance(e, event_type)]

    def clear(self) -> None:
        self.events.clear()


class NullBus:
    """Bus that discards events (used when no observation is needed)."""

    def publish(self, event: object) -> None:  # noqa: D401
        pass
