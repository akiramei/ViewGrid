"""Event-emission tests.

Verifies 30-design.md §4.1 (Event names + payload shape) and §6.1 ("Event
emission tested independently of state change").

Strategy: every successful UC publishes exactly one Event of the canonical
type, with the documented payload. Every FAILED UC publishes ZERO events
(verified in test_no_event_on_failure).
"""

from __future__ import annotations

from uuid import uuid4

import pytest

from grid_composition import (
    CellPosition,
    Conflict,
    OccupySize,
    OutOfBounds,
    PixelSize,
)
from grid_composition.domain import Axis, OrderOperation
from grid_composition.events import (
    GridCanvasCreated,
    GridDimensionsChanged,
    PlacementCreated,
    PlacementMoved,
    PlacementOccupancyResized,
    PlacementOrderChanged,
    PlacementRemoved,
    PlacementsSwapped,
    RowColumnLockToggled,
    RowColumnWeightsChanged,
)

from tests.conftest import make_copy


class TestSuccessfulUcsEmitEvents:
    def test_uc01_emits_grid_canvas_created(self, ucs, bus):
        g = ucs.create.execute("g", 2, 2, PixelSize(100, 100))
        evs = [e for e in bus.events if isinstance(e, GridCanvasCreated)]
        assert len(evs) == 1
        assert evs[0].grid_id == g.id
        assert evs[0].snapshot == g

    def test_uc02_emits_grid_dimensions_changed(self, ucs, bus, grid_3x3):
        bus.events.clear()
        ucs.change_dims.execute(grid_3x3.id, 4, 4)
        evs = [e for e in bus.events if isinstance(e, GridDimensionsChanged)]
        assert len(evs) == 1
        assert evs[0].before["rows"] == 3 and evs[0].after["rows"] == 4

    def test_uc03_emits_weights_changed(self, ucs, bus, grid_3x3):
        bus.events.clear()
        ucs.change_weights.execute(grid_3x3.id, Axis.COL, [1, 2, 1])
        evs = [e for e in bus.events if isinstance(e, RowColumnWeightsChanged)]
        assert len(evs) == 1
        assert evs[0].axis is Axis.COL
        assert evs[0].after_weights == (1, 2, 1)

    def test_uc04_emits_lock_toggled(self, ucs, bus, grid_3x3):
        bus.events.clear()
        ucs.toggle_lock.execute(grid_3x3.id, Axis.ROW, 1)
        evs = [e for e in bus.events if isinstance(e, RowColumnLockToggled)]
        assert len(evs) == 1
        assert evs[0].after_state is True

    def test_uc05_emits_placement_created(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        bus.events.clear()
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        evs = [e for e in bus.events if isinstance(e, PlacementCreated)]
        assert len(evs) == 1
        assert evs[0].snapshot == p

    def test_uc06_emits_placement_moved(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        bus.events.clear()
        ucs.move.execute(p.id, CellPosition(2, 2))
        evs = [e for e in bus.events if isinstance(e, PlacementMoved)]
        assert len(evs) == 1
        assert evs[0].before_position == CellPosition(0, 0)
        assert evs[0].after_position == CellPosition(2, 2)

    def test_uc07_emits_placements_swapped(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        bus.events.clear()
        ucs.swap.execute(a.id, b.id)
        evs = [e for e in bus.events if isinstance(e, PlacementsSwapped)]
        assert len(evs) == 1
        assert evs[0].before_a == CellPosition(0, 0)
        assert evs[0].before_b == CellPosition(2, 2)

    def test_uc08_emits_placement_resized(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        bus.events.clear()
        ucs.resize.execute(p.id, OccupySize(2, 2))
        evs = [e for e in bus.events if isinstance(e, PlacementOccupancyResized)]
        assert len(evs) == 1
        assert evs[0].before_size == OccupySize(1, 1)
        assert evs[0].after_size == OccupySize(2, 2)

    def test_uc09_emits_placement_order_changed(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        bus.events.clear()
        ucs.change_order.execute(a.id, OrderOperation.BringToFront)
        evs = [e for e in bus.events if isinstance(e, PlacementOrderChanged)]
        assert len(evs) == 1
        assert evs[0].after_order_map[a.id] == 2

    def test_uc10_emits_placement_removed_with_compact_map(
        self, ucs, bus, grid_3x3, copies
    ):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 0), OccupySize(1, 1))
        c = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 0), OccupySize(1, 1))
        bus.events.clear()
        ucs.remove.execute(b.id)
        evs = [e for e in bus.events if isinstance(e, PlacementRemoved)]
        assert len(evs) == 1
        assert evs[0].snapshot_before.id == b.id
        # After removing b (order 2), a stays at 1, c moves from 3 -> 2.
        assert evs[0].compacted_order_map[a.id] == 1
        assert evs[0].compacted_order_map[c.id] == 2

    def test_uc11_emits_nothing(self, ucs, bus, grid_3x3):
        bus.events.clear()
        ucs.list_p.execute(grid_3x3.id)
        assert bus.events == []


class TestFailedUcsEmitNothing:
    """30-design.md §4.2: 'イベントは状態変更が成功した時にのみ発行する'."""

    def test_failed_place_emits_nothing(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        bus.events.clear()
        with pytest.raises(OutOfBounds):
            ucs.place.execute(
                grid_3x3.id, cid, CellPosition(2, 0), OccupySize(2, 1)
            )
        assert bus.events == []

    def test_failed_move_emits_nothing(self, ucs, bus, grid_3x3, copies):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        bus.events.clear()
        with pytest.raises(Conflict):
            ucs.move.execute(a.id, CellPosition(2, 2))
        assert bus.events == []

    def test_failed_swap_does_not_mutate_or_emit(
        self, ucs, bus, grid_3x3, copies
    ):
        cid = make_copy(copies)
        a = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(2, 2))
        b = ucs.place.execute(grid_3x3.id, cid, CellPosition(2, 2), OccupySize(1, 1))
        bus.events.clear()
        with pytest.raises(OutOfBounds):
            ucs.swap.execute(a.id, b.id)
        # No PlacementsSwapped event, and positions unchanged.
        listed = ucs.list_p.execute(grid_3x3.id)
        positions = {p.id: p.position for p in listed}
        assert positions[a.id] == CellPosition(0, 0)
        assert positions[b.id] == CellPosition(2, 2)
        assert all(not isinstance(e, PlacementsSwapped) for e in bus.events)
