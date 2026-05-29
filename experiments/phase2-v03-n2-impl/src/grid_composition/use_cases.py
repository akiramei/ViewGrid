"""GRID_COMPOSITION UseCases (C-UC-CONTAINER: GridCompositionUseCases).

Implements UC-01..UC-11 and the *pre-loaded* read projection
get_grid_layout (C-CONSUMER-PORTS, v0.3 up-front) -- built in at n=2 even
though the RENDERING consumer does not exist yet.
"""
from __future__ import annotations

import uuid
from dataclasses import replace

from shared.events import EventBus, NullBus
from shared.ports import ImageCopyExistencePort
from shared.render_contracts import GridLayout, PlacementView
from shared.result import Err, Ok, Result
from shared.value_objects import OccupySize, PixelSize
from . import events as ev
from . import failures as fail
from .domain import (
    CellPosition,
    GridCanvas,
    Placement,
    find_conflicts,
    fits_within_grid,
    occupied_cells,
)
from .enums import Axis, OrderOperation
from .repositories import InMemoryGridCanvasRepository, InMemoryPlacementRepository


def _grid_snapshot(g: GridCanvas) -> dict:
    return {
        "id": g.id,
        "name": g.name,
        "grid_rows": g.grid_rows,
        "grid_cols": g.grid_cols,
        "col_weights": g.col_weights,
        "row_weights": g.row_weights,
        "col_locked": g.col_locked,
        "row_locked": g.row_locked,
        "canvas_size": (g.canvas_size.width, g.canvas_size.height),
    }


def _placement_snapshot(p: Placement) -> dict:
    return {
        "id": p.id,
        "grid_id": p.grid_id,
        "copy_id": p.copy_id,
        "position": (p.position.x, p.position.y),
        "occupy_size": (p.occupy_size.width, p.occupy_size.height),
        "placement_order": p.placement_order,
    }


class GridCompositionUseCases:
    def __init__(
        self,
        grid_repo: InMemoryGridCanvasRepository | None = None,
        placement_repo: InMemoryPlacementRepository | None = None,
        image_copy_existence: ImageCopyExistencePort | None = None,
        bus: EventBus | None = None,
    ) -> None:
        self._grids = grid_repo or InMemoryGridCanvasRepository()
        self._placements = placement_repo or InMemoryPlacementRepository()
        self._image_copy_existence = image_copy_existence
        self._bus = bus or NullBus()

    # ------------------------------------------------------------------
    # UC-01 CreateGridCanvas
    # ------------------------------------------------------------------
    def create_grid_canvas(
        self, name: str, grid_rows: int, grid_cols: int, canvas_size: PixelSize
    ) -> Result[GridCanvas, object]:
        try:
            grid = GridCanvas.create(name, grid_rows, grid_cols, canvas_size)
        except (ValueError, TypeError) as exc:
            return Err(fail.InvalidDimensions(detail=str(exc)))
        self._grids.save(grid)
        self._bus.publish(ev.GridCanvasCreated(grid_id=grid.id, snapshot=_grid_snapshot(grid)))
        return Ok(grid)

    # ------------------------------------------------------------------
    # UC-02 ChangeGridDimensions
    # ------------------------------------------------------------------
    def change_grid_dimensions(
        self, grid_id: uuid.UUID, new_grid_rows: int, new_grid_cols: int
    ) -> Result[GridCanvas, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=grid_id))
        if (
            isinstance(new_grid_rows, bool)
            or isinstance(new_grid_cols, bool)
            or not isinstance(new_grid_rows, int)
            or not isinstance(new_grid_cols, int)
            or new_grid_rows < 1
            or new_grid_cols < 1
        ):
            return Err(fail.InvalidDimensions(detail="new dimensions must be positive ints"))

        placements = self._placements.get_by_grid(grid_id)

        # (i) verify all existing placements fit + don't overlap under new dims.
        orphaned = [
            p.id
            for p in placements
            if not fits_within_grid(p.position, p.occupy_size, new_grid_rows, new_grid_cols)
        ]
        if orphaned:
            return Err(fail.WouldOrphanPlacements(orphaned_placement_ids=tuple(orphaned)))

        # Defensive overlap check (theoretically impossible since positions unchanged).
        for p in placements:
            cells = occupied_cells(p.position, p.occupy_size)
            conflicts = find_conflicts(cells, placements, exclude_ids={p.id})
            if conflicts:
                return Err(fail.WouldConflict(conflicting_placement_ids=tuple(conflicts)))

        # (iii) Fit-adjust weight arrays (R-05, R-08 lock-respecting).
        try:
            new_col_weights, new_col_locked = self._fit(
                grid.col_weights, grid.col_locked, new_grid_cols
            )
            new_row_weights, new_row_locked = self._fit(
                grid.row_weights, grid.row_locked, new_grid_rows
            )
        except _FitImpossible as exc:
            return Err(fail.WouldOrphanPlacements(orphaned_placement_ids=tuple(exc.locked_ids)))

        before = _grid_snapshot(grid)
        updated = grid.touched(
            grid_rows=new_grid_rows,
            grid_cols=new_grid_cols,
            col_weights=new_col_weights,
            row_weights=new_row_weights,
            col_locked=new_col_locked,
            row_locked=new_row_locked,
        )
        self._grids.save(updated)
        self._bus.publish(
            ev.GridDimensionsChanged(grid_id=grid_id, before=before, after=_grid_snapshot(updated))
        )
        return Ok(updated)

    @staticmethod
    def _fit(
        weights: tuple[int, ...], locked: tuple[bool, ...], target: int
    ) -> tuple[tuple[int, ...], tuple[bool, ...]]:
        # R-08: locked indices are skipped in fit adjustment. Deterministic.
        if target == len(weights):
            return weights, locked
        if target > len(weights):
            extra = target - len(weights)
            return weights + tuple([1] * extra), locked + tuple([False] * extra)
        # target < len: remove unlocked elements from the tail first.
        w = list(weights)
        lk = list(locked)
        removable = len(weights) - target
        idx = len(w) - 1
        removed = 0
        while removed < removable and idx >= 0:
            if not lk[idx]:
                del w[idx]
                del lk[idx]
                removed += 1
            idx -= 1
        if removed < removable:
            # Not enough unlocked elements to shrink -> would require removing locked.
            locked_ids: list[uuid.UUID] = []
            raise _FitImpossible(locked_ids)
        return tuple(w), tuple(lk)

    # ------------------------------------------------------------------
    # UC-03 ChangeRowColumnWeights
    # ------------------------------------------------------------------
    def change_row_column_weights(
        self, grid_id: uuid.UUID, axis: Axis, weights: tuple[int, ...]
    ) -> Result[GridCanvas, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=grid_id))
        weights = tuple(weights)
        expected = grid.grid_cols if axis is Axis.Col else grid.grid_rows
        if len(weights) != expected:
            return Err(fail.InvalidWeights(detail=f"length {len(weights)} != {expected}"))
        for w in weights:
            if isinstance(w, bool) or not isinstance(w, int) or w < 1:
                return Err(fail.InvalidWeights(detail="weights must be positive ints"))
        before = grid.col_weights if axis is Axis.Col else grid.row_weights
        if axis is Axis.Col:
            updated = grid.touched(col_weights=weights)
        else:
            updated = grid.touched(row_weights=weights)
        self._grids.save(updated)
        self._bus.publish(
            ev.RowColumnWeightsChanged(
                grid_id=grid_id, axis=axis.value, before_weights=before, after_weights=weights
            )
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-04 ToggleRowColumnLock
    # ------------------------------------------------------------------
    def toggle_row_column_lock(
        self, grid_id: uuid.UUID, axis: Axis, index: int
    ) -> Result[GridCanvas, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=grid_id))
        length = grid.grid_cols if axis is Axis.Col else grid.grid_rows
        if isinstance(index, bool) or not isinstance(index, int) or index < 0 or index >= length:
            return Err(fail.InvalidIndex(axis=axis.value, index=index))
        if axis is Axis.Col:
            locked = list(grid.col_locked)
            locked[index] = not locked[index]
            after_state = locked[index]
            updated = grid.touched(col_locked=tuple(locked))
        else:
            locked = list(grid.row_locked)
            locked[index] = not locked[index]
            after_state = locked[index]
            updated = grid.touched(row_locked=tuple(locked))
        self._grids.save(updated)
        self._bus.publish(
            ev.RowColumnLockToggled(
                grid_id=grid_id, axis=axis.value, index=index, after_state=after_state
            )
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-05 PlaceImageCopy
    # ------------------------------------------------------------------
    def place_image_copy(
        self,
        grid_id: uuid.UUID,
        copy_id: uuid.UUID,
        position: CellPosition,
        occupy_size: OccupySize,
    ) -> Result[Placement, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=grid_id))
        # ImageCopyExists precondition via the shared Port (no adapter).
        if self._image_copy_existence is not None and not self._image_copy_existence.exists(copy_id):
            return Err(fail.UnknownCopyId(copy_id=copy_id))
        if not fits_within_grid(position, occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(
                fail.OutOfBounds(
                    attempted_position=(position.x, position.y),
                    occupy_size=(occupy_size.width, occupy_size.height),
                )
            )
        placements = self._placements.get_by_grid(grid_id)
        cells = occupied_cells(position, occupy_size)
        conflicts = find_conflicts(cells, placements, exclude_ids=set())
        if conflicts:
            return Err(fail.Conflict(conflicting_placement_ids=tuple(conflicts)))
        next_order = max((p.placement_order for p in placements), default=0) + 1
        placement = Placement(
            id=uuid.uuid4(),
            grid_id=grid_id,
            copy_id=copy_id,
            position=position,
            occupy_size=occupy_size,
            placement_order=next_order,
        )
        self._placements.save(placement)
        self._bus.publish(
            ev.PlacementCreated(placement_id=placement.id, snapshot=_placement_snapshot(placement))
        )
        return Ok(placement)

    # ------------------------------------------------------------------
    # UC-06 MovePlacement
    # ------------------------------------------------------------------
    def move_placement(
        self, placement_id: uuid.UUID, new_position: CellPosition
    ) -> Result[Placement, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(fail.NotFound(entity_kind="Placement", entity_id=placement_id))
        grid = self._grids.get_by_id(placement.grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=placement.grid_id))
        if not fits_within_grid(new_position, placement.occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(
                fail.OutOfBounds(
                    attempted_position=(new_position.x, new_position.y),
                    occupy_size=(placement.occupy_size.width, placement.occupy_size.height),
                )
            )
        others = self._placements.get_by_grid(placement.grid_id)
        cells = occupied_cells(new_position, placement.occupy_size)
        conflicts = find_conflicts(cells, others, exclude_ids={placement.id})
        if conflicts:
            return Err(fail.Conflict(conflicting_placement_ids=tuple(conflicts)))
        before = (placement.position.x, placement.position.y)
        updated = replace(placement, position=new_position)
        self._placements.save(updated)
        self._bus.publish(
            ev.PlacementMoved(
                placement_id=placement_id,
                before_position=before,
                after_position=(new_position.x, new_position.y),
            )
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-07 SwapPlacements
    # ------------------------------------------------------------------
    def swap_placements(
        self, placement_id_a: uuid.UUID, placement_id_b: uuid.UUID
    ) -> Result[tuple[Placement, Placement], object]:
        a = self._placements.get_by_id(placement_id_a)
        if a is None:
            return Err(fail.NotFound(entity_kind="Placement", entity_id=placement_id_a))
        b = self._placements.get_by_id(placement_id_b)
        if b is None:
            return Err(fail.NotFound(entity_kind="Placement", entity_id=placement_id_b))
        grid = self._grids.get_by_id(a.grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=a.grid_id))

        # New positions are swapped; occupy sizes stay with each placement.
        a_new_pos, b_new_pos = b.position, a.position
        # (ii) R-01 on both new positions.
        if not fits_within_grid(a_new_pos, a.occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(
                fail.OutOfBounds(
                    attempted_position=(a_new_pos.x, a_new_pos.y),
                    occupy_size=(a.occupy_size.width, a.occupy_size.height),
                )
            )
        if not fits_within_grid(b_new_pos, b.occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(
                fail.OutOfBounds(
                    attempted_position=(b_new_pos.x, b_new_pos.y),
                    occupy_size=(b.occupy_size.width, b.occupy_size.height),
                )
            )
        others = self._placements.get_by_grid(a.grid_id)
        a_cells = occupied_cells(a_new_pos, a.occupy_size)
        b_cells = occupied_cells(b_new_pos, b.occupy_size)
        # (iii) R-02 against existing, excluding both.
        exclude = {a.id, b.id}
        conflicts = set(find_conflicts(a_cells, others, exclude_ids=exclude))
        conflicts |= set(find_conflicts(b_cells, others, exclude_ids=exclude))
        # (iv) post-swap A-B mutual intersection check (W-3 / AT-03).
        if a_cells & b_cells:
            conflicts |= {a.id, b.id}
        if conflicts:
            return Err(fail.Conflict(conflicting_placement_ids=tuple(conflicts)))
        before_a = (a.position.x, a.position.y)
        before_b = (b.position.x, b.position.y)
        new_a = replace(a, position=a_new_pos)
        new_b = replace(b, position=b_new_pos)
        self._placements.save(new_a)
        self._placements.save(new_b)
        self._bus.publish(
            ev.PlacementsSwapped(
                placement_id_a=a.id,
                placement_id_b=b.id,
                before_a=before_a,
                before_b=before_b,
            )
        )
        return Ok((new_a, new_b))

    # ------------------------------------------------------------------
    # UC-08 ResizePlacementOccupancy
    # ------------------------------------------------------------------
    def resize_placement_occupancy(
        self, placement_id: uuid.UUID, new_occupy_size: OccupySize
    ) -> Result[Placement, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(fail.NotFound(entity_kind="Placement", entity_id=placement_id))
        grid = self._grids.get_by_id(placement.grid_id)
        if grid is None:
            return Err(fail.NotFound(entity_kind="GridCanvas", entity_id=placement.grid_id))
        if not fits_within_grid(placement.position, new_occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(
                fail.OutOfBounds(
                    attempted_position=(placement.position.x, placement.position.y),
                    occupy_size=(new_occupy_size.width, new_occupy_size.height),
                )
            )
        others = self._placements.get_by_grid(placement.grid_id)
        cells = occupied_cells(placement.position, new_occupy_size)
        conflicts = find_conflicts(cells, others, exclude_ids={placement.id})
        if conflicts:
            return Err(fail.Conflict(conflicting_placement_ids=tuple(conflicts)))
        before = (placement.occupy_size.width, placement.occupy_size.height)
        updated = replace(placement, occupy_size=new_occupy_size)
        self._placements.save(updated)
        self._bus.publish(
            ev.PlacementOccupancyResized(
                placement_id=placement_id,
                before_size=before,
                after_size=(new_occupy_size.width, new_occupy_size.height),
            )
        )
        return Ok(updated)

    # ------------------------------------------------------------------
    # UC-09 ChangePlacementOrder
    # ------------------------------------------------------------------
    def change_placement_order(
        self,
        placement_id: uuid.UUID,
        operation: OrderOperation,
        order_value: int | None = None,
    ) -> Result[Placement, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(fail.NotFound(entity_kind="Placement", entity_id=placement_id))

        if operation is OrderOperation.SetOrder:
            if order_value is None or isinstance(order_value, bool) or not isinstance(order_value, int):
                return Err(
                    fail.InvalidOrderValue(
                        detail="SetOrder requires an integer order_value",
                        attempted_value=order_value if isinstance(order_value, int) else None,
                    )
                )
        else:
            if order_value is not None:
                return Err(
                    fail.InvalidOrderValue(
                        detail=f"order_value must be None for {operation.value}",
                        attempted_value=order_value,
                    )
                )

        siblings = self._placements.get_by_grid(placement.grid_id)
        n = len(siblings)
        # current ordering by placement_order ascending.
        ordered = sorted(siblings, key=lambda p: p.placement_order)
        ids = [p.id for p in ordered]
        cur_index = ids.index(placement_id)  # 0-based position in z order

        if operation is OrderOperation.BringToFront:
            target_index = n - 1
        elif operation is OrderOperation.SendToBack:
            target_index = 0
        elif operation is OrderOperation.MoveForward:
            target_index = min(cur_index + 1, n - 1)
        elif operation is OrderOperation.MoveBackward:
            target_index = max(cur_index - 1, 0)
        else:  # SetOrder
            assert order_value is not None
            if order_value < 1 or order_value > n:
                return Err(
                    fail.InvalidOrderValue(
                        detail=f"order_value {order_value} out of range 1..{n}",
                        attempted_value=order_value,
                    )
                )
            target_index = order_value - 1

        before_map = {p.id: p.placement_order for p in ordered}

        # Re-sequence: remove then insert at target index, assign dense 1..N.
        ids.pop(cur_index)
        ids.insert(target_index, placement_id)
        after_map: dict[uuid.UUID, int] = {}
        by_id = {p.id: p for p in ordered}
        result_placement = placement
        for new_order, pid in enumerate(ids, start=1):
            current = by_id[pid]
            if current.placement_order != new_order:
                current = replace(current, placement_order=new_order)
                self._placements.save(current)
            after_map[pid] = new_order
            if pid == placement_id:
                result_placement = current

        self._bus.publish(
            ev.PlacementOrderChanged(
                grid_id=placement.grid_id,
                before_order_map=before_map,
                after_order_map=after_map,
            )
        )
        return Ok(result_placement)

    # ------------------------------------------------------------------
    # UC-10 RemovePlacement
    # ------------------------------------------------------------------
    def remove_placement(self, placement_id: uuid.UUID) -> Result[uuid.UUID, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(fail.NotFound(entity_kind="Placement", entity_id=placement_id))
        snapshot = _placement_snapshot(placement)
        self._placements.delete(placement_id)
        # R-09: compact remaining orders to 1..N.
        remaining = sorted(
            self._placements.get_by_grid(placement.grid_id),
            key=lambda p: p.placement_order,
        )
        compacted: dict[uuid.UUID, int] = {}
        for new_order, p in enumerate(remaining, start=1):
            if p.placement_order != new_order:
                p = replace(p, placement_order=new_order)
                self._placements.save(p)
            compacted[p.id] = new_order
        self._bus.publish(
            ev.PlacementRemoved(
                placement_id=placement_id,
                snapshot_before=snapshot,
                compacted_order_map=compacted,
            )
        )
        return Ok(placement_id)

    # ------------------------------------------------------------------
    # UC-11 ListPlacements
    # ------------------------------------------------------------------
    def list_placements(self, grid_id: uuid.UUID) -> list[Placement]:
        placements = self._placements.get_by_grid(grid_id)
        return sorted(placements, key=lambda p: p.placement_order)

    # ------------------------------------------------------------------
    # C-CONSUMER-PORTS (v0.3, pre-loaded): GridLayoutPort.get_grid_layout
    # GRID natively satisfies the read port -- no standalone adapter.
    # ------------------------------------------------------------------
    def get_grid_layout(self, grid_id: uuid.UUID) -> GridLayout | None:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return None
        placements = sorted(
            self._placements.get_by_grid(grid_id), key=lambda p: p.placement_order
        )
        views = tuple(
            PlacementView(
                copy_id=p.copy_id,
                x=p.position.x,
                y=p.position.y,
                occupy_w=p.occupy_size.width,
                occupy_h=p.occupy_size.height,
                order=p.placement_order,
            )
            for p in placements
        )
        return GridLayout(
            grid_rows=grid.grid_rows,
            grid_cols=grid.grid_cols,
            col_weights=grid.col_weights,
            row_weights=grid.row_weights,
            canvas_w=grid.canvas_size.width,
            canvas_h=grid.canvas_size.height,
            placements=views,
        )


class _FitImpossible(Exception):
    def __init__(self, locked_ids: list[uuid.UUID]) -> None:
        super().__init__("fit impossible without removing locked axis")
        self.locked_ids = locked_ids
