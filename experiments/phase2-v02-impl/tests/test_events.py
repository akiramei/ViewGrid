"""Event emission tests.

Per 30-design.md §4 IMPORTANT: events fire only on success, and emission
must be testably independent of state change.
"""

from __future__ import annotations

from grid_composition import (
    Axis,
    CellPosition,
    GridCanvasCreated,
    GridDimensionsChanged,
    OccupySize,
    OrderOperation,
    PixelSize,
    PlacementCreated,
    PlacementMoved,
    PlacementOccupancyResized,
    PlacementOrderChanged,
    PlacementRemoved,
    PlacementsSwapped,
    RowColumnLockToggled,
    RowColumnWeightsChanged,
    new_id,
)


def test_uc01_emits_grid_canvas_created(uc, bus):
    uc.create_grid_canvas(name="x", grid_rows=2, grid_cols=2, canvas_size=PixelSize(10, 10))
    assert len(bus.events) == 1 and isinstance(bus.events[0], GridCanvasCreated)


def test_uc01_failure_emits_nothing(uc, bus):
    uc.create_grid_canvas(name="x", grid_rows=0, grid_cols=2, canvas_size=PixelSize(10, 10))
    assert bus.events == []


def test_uc02_emits_dim_changed(uc, bus, small_grid):
    uc.change_grid_dimensions(grid_id=small_grid.id, new_grid_rows=4, new_grid_cols=4)
    assert any(isinstance(e, GridDimensionsChanged) for e in bus.events)


def test_uc03_emits_weights_changed(uc, bus, small_grid):
    uc.change_row_column_weights(grid_id=small_grid.id, axis=Axis.COL, weights=[2, 1, 1])
    assert any(isinstance(e, RowColumnWeightsChanged) for e in bus.events)


def test_uc04_emits_lock_toggled(uc, bus, small_grid):
    uc.toggle_row_column_lock(grid_id=small_grid.id, axis=Axis.COL, index=0)
    assert any(isinstance(e, RowColumnLockToggled) for e in bus.events)


def test_uc05_emits_placement_created(uc, bus, small_grid, a_copy_id):
    uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    )
    assert sum(1 for e in bus.events if isinstance(e, PlacementCreated)) == 1


def test_uc05_failure_emits_no_placement_created(uc, bus, small_grid):
    # Unknown copy id
    uc.place_image_copy(
        grid_id=small_grid.id, copy_id=new_id(),
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    )
    assert all(not isinstance(e, PlacementCreated) for e in bus.events)


def test_uc06_emits_moved(uc, bus, small_grid, a_copy_id):
    p = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    ).value
    bus.clear()
    uc.move_placement(placement_id=p.id, new_position=CellPosition(1, 1))
    assert any(isinstance(e, PlacementMoved) for e in bus.events)


def test_uc07_emits_swapped(uc, bus, small_grid, a_copy_id):
    a = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    ).value
    b = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(2, 2), occupy_size=OccupySize(1, 1),
    ).value
    bus.clear()
    uc.swap_placements(placement_id_a=a.id, placement_id_b=b.id)
    assert any(isinstance(e, PlacementsSwapped) for e in bus.events)


def test_uc08_emits_resized(uc, bus, small_grid, a_copy_id):
    p = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    ).value
    bus.clear()
    uc.resize_placement_occupancy(placement_id=p.id, new_occupy_size=OccupySize(2, 2))
    assert any(isinstance(e, PlacementOccupancyResized) for e in bus.events)


def test_uc09_emits_order_changed(uc, bus, small_grid, a_copy_id):
    ps = [
        uc.place_image_copy(
            grid_id=small_grid.id, copy_id=a_copy_id,
            position=CellPosition(x, 0), occupy_size=OccupySize(1, 1),
        ).value
        for x in (0, 1, 2)
    ]
    bus.clear()
    uc.change_placement_order(
        placement_id=ps[0].id, operation=OrderOperation.BRING_TO_FRONT
    )
    assert any(isinstance(e, PlacementOrderChanged) for e in bus.events)


def test_uc10_emits_removed(uc, bus, small_grid, a_copy_id):
    p = uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(1, 1),
    ).value
    bus.clear()
    uc.remove_placement(placement_id=p.id)
    assert any(isinstance(e, PlacementRemoved) for e in bus.events)


def test_failure_paths_emit_no_events(uc, bus, small_grid, a_copy_id):
    uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(0, 0), occupy_size=OccupySize(2, 1),
    )
    bus.clear()
    # Conflict
    uc.place_image_copy(
        grid_id=small_grid.id, copy_id=a_copy_id,
        position=CellPosition(1, 0), occupy_size=OccupySize(1, 1),
    )
    assert bus.events == []
