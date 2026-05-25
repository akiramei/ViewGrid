"""Canonical Events + simple in-process EventBus.

Event names and emission timing are FORBIDDEN to change per
40-ai-implementation-prompt.md FORBIDDEN. Each Event class corresponds 1:1
to the entries in 21-grid-composition.yaml §events.

The EventBus is intentionally minimal: synchronous, in-process,
list-of-subscribers. This satisfies 30-design.md §4.2 ("発行機構は規定しな
い... ローカル同期発行を推奨"). For tests, a `RecordingBus` is provided
that captures all events into a list — required by 30-design.md §6.1
("Event emission tested independently of state change").

Decision Ownership: events live in their own module so that no use case is
forced to import a UI / persistence concept. They are pure value types.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Callable, Mapping
from uuid import UUID

from .domain import Axis, CellPosition, GridCanvas, OccupySize, Placement


# -- Event types ------------------------------------------------------------


class Event:
    """Marker base type. Use cases emit subclasses."""


@dataclass(frozen=True)
class GridCanvasCreated(Event):
    """UC-01 success. snapshot = the GridCanvas at creation time."""

    grid_id: UUID
    snapshot: GridCanvas


@dataclass(frozen=True)
class GridDimensionsChanged(Event):
    """UC-02 success. `before` / `after` carry the full dimension+weight+lock
    tuple per 30-design.md §4.1 ('before { rows, cols, col_weights,
    row_weights, col_locked, row_locked }')."""

    grid_id: UUID
    before: Mapping[str, Any]
    after: Mapping[str, Any]


@dataclass(frozen=True)
class RowColumnWeightsChanged(Event):
    """UC-03 success."""

    grid_id: UUID
    axis: Axis
    before_weights: tuple[int, ...]
    after_weights: tuple[int, ...]


@dataclass(frozen=True)
class RowColumnLockToggled(Event):
    """UC-04 success."""

    grid_id: UUID
    axis: Axis
    index: int
    after_state: bool


@dataclass(frozen=True)
class PlacementCreated(Event):
    """UC-05 success."""

    placement_id: UUID
    snapshot: Placement


@dataclass(frozen=True)
class PlacementMoved(Event):
    """UC-06 success."""

    placement_id: UUID
    before_position: CellPosition
    after_position: CellPosition


@dataclass(frozen=True)
class PlacementsSwapped(Event):
    """UC-07 success. before_a/before_b are the CellPositions before swap."""

    placement_id_a: UUID
    placement_id_b: UUID
    before_a: CellPosition
    before_b: CellPosition


@dataclass(frozen=True)
class PlacementOccupancyResized(Event):
    """UC-08 success."""

    placement_id: UUID
    before_size: OccupySize
    after_size: OccupySize


@dataclass(frozen=True)
class PlacementOrderChanged(Event):
    """UC-09 success. Maps are {placement_id -> order}."""

    grid_id: UUID
    before_order_map: Mapping[UUID, int]
    after_order_map: Mapping[UUID, int]


@dataclass(frozen=True)
class PlacementRemoved(Event):
    """UC-10 success. snapshot_before = the Placement at the moment of
    removal; compacted_order_map = the post-compact order assignments per
    R-09."""

    placement_id: UUID
    snapshot_before: Placement
    compacted_order_map: Mapping[UUID, int]


# -- Bus --------------------------------------------------------------------


class EventBus:
    """Synchronous in-process pub/sub.

    `publish` calls every subscriber in registration order. Subscribers see
    the same Event instance; do not mutate it (events are frozen
    dataclasses, so this is enforced).

    By design, the bus does NOT decide whether to emit — that is the
    UseCase's job. The bus only routes.
    """

    def __init__(self) -> None:
        self._subscribers: list[Callable[[Event], None]] = []

    def subscribe(self, handler: Callable[[Event], None]) -> Callable[[], None]:
        """Register a handler. Returns an unsubscribe callable."""

        self._subscribers.append(handler)

        def _unsubscribe() -> None:
            try:
                self._subscribers.remove(handler)
            except ValueError:
                pass

        return _unsubscribe

    def publish(self, event: Event) -> None:
        for h in list(self._subscribers):
            h(event)


@dataclass
class RecordingBus(EventBus):
    """Test helper. Captures every event into `events` for assertion."""

    events: list[Event] = field(default_factory=list)

    def __post_init__(self) -> None:
        super().__init__()
        self.subscribe(self.events.append)
