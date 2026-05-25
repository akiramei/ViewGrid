"""Stub for HISTORY_MANAGEMENT.

Per ``20-capability-bom.md`` §8 / Events, HISTORY_MANAGEMENT subscribes to ALL
IMAGE_VARIANT_MANAGEMENT events. This stub records them for inspection.
"""

from __future__ import annotations

from ..events import EventBus


class HistoryManagementStub:
    def __init__(self, *, event_bus: EventBus) -> None:
        self._log: list[object] = []
        event_bus.subscribe(self._log.append)

    @property
    def log(self) -> list[object]:
        return list(self._log)
