"""HISTORY_MANAGEMENT — stub boundary only.

GRID_COMPOSITION emits Events (see grid_composition.events). HISTORY would
subscribe to those events and decide on Undo/Redo granularity and
coalescing. Those decisions are owned by HISTORY_MANAGEMENT, NOT by
GRID_COMPOSITION (see 20-capability-bom.md §8.2).

This stub shows the subscription shape only.
"""

from __future__ import annotations

from typing import Callable, Protocol

from grid_composition.events import Event, EventBus


class HistoryRecorder(Protocol):
    def record(self, event: Event) -> None: ...


def attach(bus: EventBus, recorder: HistoryRecorder) -> Callable[[], None]:
    """Wire `recorder` to receive every GRID_COMPOSITION event.

    The unsubscribe callable is returned. The recorder's behavior (what to
    treat as a single Undo step, etc.) is the HISTORY capability's
    decision — not ours.
    """

    return bus.subscribe(recorder.record)
