"""Domain events emitted by the UseCase layer (21-yaml §events).

A minimal in-process synchronous event bus is provided as
:class:`RecordingBus`. The bus is wired by the test code so emission
can be observed independently of state change (30-design §4 IMPORTANT
note).

MUST_DECIDE_AND_DOCUMENT: in-process synchronous delivery. Events are
appended to the bus only when the corresponding UseCase succeeded.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Protocol, Tuple, Union

from .entities import GridCanvas, Placement
from .identity import Id
from .value_objects import Axis, CellPosition, OccupySize


@dataclass(frozen=True)
class GridCanvasCreated:
    grid_id: Id
    snapshot: GridCanvas


@dataclass(frozen=True)
class GridDimensionsChanged:
    grid_id: Id
    before: GridCanvas
    after: GridCanvas


@dataclass(frozen=True)
class RowColumnWeightsChanged:
    grid_id: Id
    axis: Axis
    before_weights: Tuple[int, ...]
    after_weights: Tuple[int, ...]


@dataclass(frozen=True)
class RowColumnLockToggled:
    grid_id: Id
    axis: Axis
    index: int
    after_state: bool


@dataclass(frozen=True)
class PlacementCreated:
    placement_id: Id
    snapshot: Placement


@dataclass(frozen=True)
class PlacementMoved:
    placement_id: Id
    before_position: CellPosition
    after_position: CellPosition


@dataclass(frozen=True)
class PlacementsSwapped:
    placement_id_a: Id
    placement_id_b: Id
    before_a: CellPosition
    before_b: CellPosition


@dataclass(frozen=True)
class PlacementOccupancyResized:
    placement_id: Id
    before_size: OccupySize
    after_size: OccupySize


@dataclass(frozen=True)
class PlacementOrderChanged:
    grid_id: Id
    before_order_map: Dict[Id, int]
    after_order_map: Dict[Id, int]


@dataclass(frozen=True)
class PlacementRemoved:
    placement_id: Id
    snapshot_before: Placement
    compacted_order_map: Dict[Id, int]


Event = Union[
    GridCanvasCreated,
    GridDimensionsChanged,
    RowColumnWeightsChanged,
    RowColumnLockToggled,
    PlacementCreated,
    PlacementMoved,
    PlacementsSwapped,
    PlacementOccupancyResized,
    PlacementOrderChanged,
    PlacementRemoved,
]


class EventBus(Protocol):
    def publish(self, event: Event) -> None: ...


@dataclass
class RecordingBus:
    """Test-friendly event bus: synchronous, records every published event.

    Per A.6, this pattern was identified as useful — we adopt it directly.
    """

    events: List[Event] = field(default_factory=list)

    def publish(self, event: Event) -> None:
        self.events.append(event)

    def clear(self) -> None:
        self.events.clear()


class NullBus:
    """Discards events. Used as default when caller does not pass one."""

    def publish(self, event: Event) -> None:  # noqa: D401
        return None
