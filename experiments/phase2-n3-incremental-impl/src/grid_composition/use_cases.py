"""GridCompositionUseCases — UC-01..UC-11 (C-UC-CONTAINER naming).

Each UseCase is a single (input -> Result) method (audit requirement). Rules
R-01/R-02 are enforced by calling the pure functions in domain.py exactly once
per UseCase. Events are published only on success, through the shared RecordingBus
(C-EVENTBUS). Identity is uuid.UUID throughout (C-IDENTITY).

Boundary (C-BOUNDARY-IFACE): UC-05 depends on an ImageCopyExistencePort and
returns UnknownCopyId when exists() is False. The Port is injected; no adapter.

Consumer read boundary (C-CONSUMER-PORTS, v0.2 / n=3): this class additionally
SATISFIES shared.ports.GridLayoutPort natively via get_grid_layout(grid_id),
which projects a GridCanvas + its placements into the neutral GridLayout DTO.
This is a native projection (an *added* read method), NOT a standalone adapter,
and it changes no existing Rule / UseCase / failure-reason semantics.
"""

from __future__ import annotations

import uuid
from datetime import datetime, timezone

from grid_composition import events as ev
from grid_composition.domain import (
    Axis,
    CellPosition,
    GridCanvas,
    OrderOperation,
    Placement,
    find_overlaps,
    fits_within_grid,
    occupied_cells,
)
from grid_composition.failures import (
    Conflict,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    NotFound,
    OutOfBounds,
    UnknownCopyId,
    WouldConflict,
    WouldOrphanPlacements,
)
from grid_composition.repositories import (
    InMemoryGridCanvasRepository,
    InMemoryPlacementRepository,
)
from shared.eventbus import RecordingBus
from shared.ports import ImageCopyExistencePort
from shared.render_contracts import GridLayout, PlacementView
from shared.result import Err, Ok, Result
from shared.value_objects import OccupySize, PixelSize


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


class GridCompositionUseCases:
    def __init__(
        self,
        grid_repo: InMemoryGridCanvasRepository,
        placement_repo: InMemoryPlacementRepository,
        image_copy_existence: ImageCopyExistencePort,
        bus: RecordingBus,
    ) -> None:
        self._grids = grid_repo
        self._placements = placement_repo
        # C-BOUNDARY-IFACE: the Port is stored directly. No adapter wraps it.
        self._copies = image_copy_existence
        self._bus = bus

    # ------------------------------------------------------------------ UC-01
    def create_grid_canvas(
        self, name: str, grid_rows: int, grid_cols: int, canvas_size: PixelSize
    ) -> Result[uuid.UUID, InvalidDimensions]:
        try:
            grid = GridCanvas.create(name, grid_rows, grid_cols, canvas_size)
        except (TypeError, ValueError) as exc:
            return Err(InvalidDimensions(detail=str(exc)))
        self._grids.save(grid)
        self._bus.publish(ev.GridCanvasCreated(grid_id=grid.id, snapshot=grid.snapshot()))
        return Ok(grid.id)

    # ------------------------------------------------------------------ UC-02
    def change_grid_dimensions(
        self, grid_id: uuid.UUID, new_grid_rows: int, new_grid_cols: int
    ) -> Result[uuid.UUID, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        # validation: positive dimensions (R-03 family) -> InvalidDimensions
        if (isinstance(new_grid_rows, bool) or isinstance(new_grid_cols, bool)
                or not isinstance(new_grid_rows, int)
                or not isinstance(new_grid_cols, int)
                or new_grid_rows < 1 or new_grid_cols < 1):
            return Err(InvalidDimensions(detail="new dimensions must be ints >= 1"))

        placements = self._placements.get_by_grid(grid_id)

        # (i) R-01 over all existing placements at the new dimensions.
        orphaned = [
            p.id for p in placements
            if not fits_within_grid(p.position, p.occupy_size, new_grid_rows, new_grid_cols)
        ]
        if orphaned:
            return Err(WouldOrphanPlacements(orphaned_placement_ids=tuple(orphaned)))

        # R-02 over the full set (defensive; should not trigger if it held before).
        for i, p in enumerate(placements):
            others = placements[:i] + placements[i + 1:]
            if find_overlaps(p.position, p.occupy_size, others):
                return Err(WouldConflict(conflicting_placement_ids=(p.id,)))

        # (iii) Fit weights/locks to new dims (R-05 + R-08 Fit adjustment).
        new_col_weights, new_col_locked = self._fit_axis(
            grid.col_weights, grid.col_locked, new_grid_cols)
        new_row_weights, new_row_locked = self._fit_axis(
            grid.row_weights, grid.row_locked, new_grid_rows)
        if new_col_weights is None or new_row_weights is None:
            # Locked elements would have to be dropped -> map to WouldOrphanPlacements
            # (per 30-design.md §R-08: no dedicated failure reason is created).
            return Err(WouldOrphanPlacements(orphaned_placement_ids=()))

        before = grid.snapshot()
        updated = GridCanvas(
            id=grid.id,
            name=grid.name,
            grid_rows=new_grid_rows,
            grid_cols=new_grid_cols,
            col_weights=new_col_weights,
            row_weights=new_row_weights,
            col_locked=new_col_locked,
            row_locked=new_row_locked,
            canvas_size=grid.canvas_size,
            created_at=grid.created_at,
            updated_at=_utc_now(),
        )
        self._grids.save(updated)
        self._bus.publish(ev.GridDimensionsChanged(
            grid_id=grid_id, before=before, after=updated.snapshot()))
        return Ok(grid_id)

    @staticmethod
    def _fit_axis(
        weights: tuple[int, ...], locked: tuple[bool, ...], target: int
    ) -> tuple[tuple[int, ...] | None, tuple[bool, ...] | None]:
        """R-08 Fit adjustment. Deterministic. Locked indices are not dropped.

        Returns (None, None) if shrinking is impossible without dropping locked
        elements (caller maps this to WouldOrphanPlacements).
        """
        n = len(weights)
        if target == n:
            return weights, locked
        if target > n:
            grow = target - n
            return weights + (1,) * grow, locked + (False,) * grow
        # target < n: drop unlocked elements from the tail, preserving locked.
        keep_idx = list(range(n))
        to_drop = n - target
        # iterate tail-first, removing unlocked indices until enough dropped
        i = n - 1
        while to_drop > 0 and i >= 0:
            if not locked[i]:
                keep_idx.remove(i)
                to_drop -= 1
            i -= 1
        if to_drop > 0:
            return None, None  # not enough unlocked elements to drop
        keep_idx.sort()
        return (tuple(weights[i] for i in keep_idx),
                tuple(locked[i] for i in keep_idx))

    # ------------------------------------------------------------------ UC-03
    def change_row_column_weights(
        self, grid_id: uuid.UUID, axis: Axis, weights: tuple[int, ...]
    ) -> Result[uuid.UUID, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        weights = tuple(weights)
        # R-04: positive ints; R-05: length matches dimension.
        for w in weights:
            if isinstance(w, bool) or not isinstance(w, int) or w < 1:
                return Err(InvalidWeights(detail="weights must be positive ints (R-04)"))
        expected = grid.grid_cols if axis is Axis.Col else grid.grid_rows
        if len(weights) != expected:
            return Err(InvalidWeights(
                detail=f"weights length {len(weights)} != dimension {expected} (R-05)"))

        before = grid.col_weights if axis is Axis.Col else grid.row_weights
        if axis is Axis.Col:
            updated = GridCanvas(**{**grid.__dict__, "col_weights": weights,
                                   "updated_at": _utc_now()})
        else:
            updated = GridCanvas(**{**grid.__dict__, "row_weights": weights,
                                   "updated_at": _utc_now()})
        self._grids.save(updated)
        self._bus.publish(ev.RowColumnWeightsChanged(
            grid_id=grid_id, axis=axis, before=before, after=weights))
        return Ok(grid_id)

    # ------------------------------------------------------------------ UC-04
    def toggle_row_column_lock(
        self, grid_id: uuid.UUID, axis: Axis, index: int
    ) -> Result[uuid.UUID, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        limit = grid.grid_cols if axis is Axis.Col else grid.grid_rows
        if (isinstance(index, bool) or not isinstance(index, int)
                or index < 0 or index >= limit):
            return Err(InvalidIndex(axis=axis.value, index=index))

        locked = list(grid.col_locked if axis is Axis.Col else grid.row_locked)
        locked[index] = not locked[index]
        after_state = locked[index]
        field = "col_locked" if axis is Axis.Col else "row_locked"
        updated = GridCanvas(**{**grid.__dict__, field: tuple(locked),
                               "updated_at": _utc_now()})
        self._grids.save(updated)
        self._bus.publish(ev.RowColumnLockToggled(
            grid_id=grid_id, axis=axis, index=index, after_state=after_state))
        return Ok(grid_id)

    # ------------------------------------------------------------------ UC-05
    def place_image_copy(
        self,
        grid_id: uuid.UUID,
        copy_id: uuid.UUID,
        position: CellPosition,
        occupy_size: OccupySize,
    ) -> Result[uuid.UUID, object]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        # C-BOUNDARY-IFACE: call the Port directly. exists() False -> UnknownCopyId.
        if not self._copies.exists(copy_id):
            return Err(UnknownCopyId(copy_id=copy_id))

        if not fits_within_grid(position, occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(OutOfBounds(attempted_position=position, occupy_size=occupy_size))

        existing = self._placements.get_by_grid(grid_id)
        conflicts = find_overlaps(position, occupy_size, existing)
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=tuple(conflicts)))

        # R-06: new order = max(existing) + 1 (1 if empty).
        next_order = max((p.placement_order for p in existing), default=0) + 1
        placement = Placement(
            id=uuid.uuid4(),
            grid_id=grid_id,
            copy_id=copy_id,
            position=position,
            occupy_size=occupy_size,
            placement_order=next_order,
            created_at=_utc_now(),
        )
        self._placements.save(placement)
        self._bus.publish(ev.PlacementCreated(
            placement_id=placement.id, snapshot=placement.snapshot()))
        return Ok(placement.id)

    # ------------------------------------------------------------------ UC-06
    def move_placement(
        self, placement_id: uuid.UUID, new_position: CellPosition
    ) -> Result[uuid.UUID, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))
        grid = self._grids.get_by_id(placement.grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=placement.grid_id))

        if not fits_within_grid(new_position, placement.occupy_size,
                                grid.grid_rows, grid.grid_cols):
            return Err(OutOfBounds(attempted_position=new_position,
                                   occupy_size=placement.occupy_size))

        # R-02: exclude self from "existing placements".
        others = [p for p in self._placements.get_by_grid(placement.grid_id)
                  if p.id != placement_id]
        conflicts = find_overlaps(new_position, placement.occupy_size, others)
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=tuple(conflicts)))

        before_position = placement.position
        moved = placement.with_position(new_position)
        self._placements.save(moved)
        self._bus.publish(ev.PlacementMoved(
            placement_id=placement_id,
            before_position=before_position, after_position=new_position))
        return Ok(placement_id)

    # ------------------------------------------------------------------ UC-07
    def swap_placements(
        self, placement_id_a: uuid.UUID, placement_id_b: uuid.UUID
    ) -> Result[uuid.UUID, object]:
        # (i) fetch both; first missing -> NotFound.
        a = self._placements.get_by_id(placement_id_a)
        if a is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id_a))
        b = self._placements.get_by_id(placement_id_b)
        if b is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id_b))

        grid = self._grids.get_by_id(a.grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=a.grid_id))

        new_a_pos = b.position  # A moves to B's old position
        new_b_pos = a.position  # B moves to A's old position

        # (ii) R-01 for both at their new positions.
        if not fits_within_grid(new_a_pos, a.occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(OutOfBounds(attempted_position=new_a_pos, occupy_size=a.occupy_size))
        if not fits_within_grid(new_b_pos, b.occupy_size, grid.grid_rows, grid.grid_cols):
            return Err(OutOfBounds(attempted_position=new_b_pos, occupy_size=b.occupy_size))

        # (iii) R-02 with BOTH excluded from "existing".
        others = [p for p in self._placements.get_by_grid(a.grid_id)
                  if p.id != placement_id_a and p.id != placement_id_b]
        conflicts = (find_overlaps(new_a_pos, a.occupy_size, others)
                     + find_overlaps(new_b_pos, b.occupy_size, others))
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=tuple(dict.fromkeys(conflicts))))

        # (iv) post-swap A-B mutual intersection check (W-3 / §2.2 UC-07).
        if occupied_cells(new_a_pos, a.occupy_size) & occupied_cells(new_b_pos, b.occupy_size):
            return Err(Conflict(conflicting_placement_ids=(a.id, b.id)))

        # (v) atomic update of both.
        before_a, before_b = a.position, b.position
        self._placements.save(a.with_position(new_a_pos))
        self._placements.save(b.with_position(new_b_pos))
        self._bus.publish(ev.PlacementsSwapped(
            placement_id_a=placement_id_a, placement_id_b=placement_id_b,
            before_a=before_a, before_b=before_b))
        return Ok(placement_id_a)

    # ------------------------------------------------------------------ UC-08
    def resize_placement_occupancy(
        self, placement_id: uuid.UUID, new_occupy_size: OccupySize
    ) -> Result[uuid.UUID, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))
        grid = self._grids.get_by_id(placement.grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=placement.grid_id))

        if not fits_within_grid(placement.position, new_occupy_size,
                                grid.grid_rows, grid.grid_cols):
            return Err(OutOfBounds(attempted_position=placement.position,
                                   occupy_size=new_occupy_size))

        others = [p for p in self._placements.get_by_grid(placement.grid_id)
                  if p.id != placement_id]
        conflicts = find_overlaps(placement.position, new_occupy_size, others)
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=tuple(conflicts)))

        before_size = placement.occupy_size
        resized = placement.with_occupy_size(new_occupy_size)
        self._placements.save(resized)
        self._bus.publish(ev.PlacementOccupancyResized(
            placement_id=placement_id, before_size=before_size, after_size=new_occupy_size))
        return Ok(placement_id)

    # ------------------------------------------------------------------ UC-09
    def change_placement_order(
        self,
        placement_id: uuid.UUID,
        operation: OrderOperation,
        order_value: int | None = None,
    ) -> Result[uuid.UUID, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))

        siblings = self._placements.get_by_grid(placement.grid_id)
        n = len(siblings)

        # order_value rules: required & in [1,N] for SetOrder; forbidden otherwise.
        if operation is OrderOperation.SetOrder:
            if (order_value is None or isinstance(order_value, bool)
                    or not isinstance(order_value, int)
                    or order_value < 1 or order_value > n):
                return Err(InvalidOrderValue(
                    detail=f"SetOrder requires order_value in 1..{n}",
                    attempted_value=order_value if isinstance(order_value, int) else None))
        else:
            if order_value is not None:
                return Err(InvalidOrderValue(
                    detail="order_value only valid for SetOrder",
                    attempted_value=order_value if isinstance(order_value, int) else None))

        before_map = {p.id: p.placement_order for p in siblings}

        # Determine the dense 1..N ordering after the operation.
        ordered = sorted(siblings, key=lambda p: p.placement_order)
        ids_in_order = [p.id for p in ordered]
        cur_index = ids_in_order.index(placement_id)

        if operation is OrderOperation.BringToFront:
            target_index = n - 1
        elif operation is OrderOperation.SendToBack:
            target_index = 0
        elif operation is OrderOperation.MoveForward:
            target_index = min(cur_index + 1, n - 1)
        elif operation is OrderOperation.MoveBackward:
            target_index = max(cur_index - 1, 0)
        else:  # SetOrder
            target_index = order_value - 1  # order is 1-based, dense

        ids_in_order.pop(cur_index)
        ids_in_order.insert(target_index, placement_id)

        # Reassign dense, unique orders 1..N (R-06 + OrdersAreDense).
        after_map: dict[uuid.UUID, int] = {}
        for new_order, pid in enumerate(ids_in_order, start=1):
            after_map[pid] = new_order
            p = self._placements.get_by_id(pid)
            if p is not None and p.placement_order != new_order:
                self._placements.save(p.with_order(new_order))

        self._bus.publish(ev.PlacementOrderChanged(
            grid_id=placement.grid_id, before_order_map=before_map, after_order_map=after_map))
        return Ok(placement_id)

    # ------------------------------------------------------------------ UC-10
    def remove_placement(
        self, placement_id: uuid.UUID
    ) -> Result[uuid.UUID, object]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))

        snapshot_before = placement.snapshot()
        grid_id = placement.grid_id
        self._placements.delete(placement_id)

        # R-09: compact survivors to dense 1..N (preserving relative order).
        survivors = sorted(self._placements.get_by_grid(grid_id),
                           key=lambda p: p.placement_order)
        compacted: dict[uuid.UUID, int] = {}
        for new_order, p in enumerate(survivors, start=1):
            compacted[p.id] = new_order
            if p.placement_order != new_order:
                self._placements.save(p.with_order(new_order))

        self._bus.publish(ev.PlacementRemoved(
            placement_id=placement_id, snapshot_before=snapshot_before,
            compacted_order_map=compacted))
        return Ok(placement_id)

    # ------------------------------------------------------------------ UC-11
    def list_placements(self, grid_id: uuid.UUID) -> list[Placement]:
        # query: no failure. Missing grid -> empty list.
        placements = self._placements.get_by_grid(grid_id)
        return sorted(placements, key=lambda p: p.placement_order)

    # =====================================================================
    # Consumer read port satisfaction (C-CONSUMER-PORTS): this IS GridLayoutPort.
    # Native projection GridCanvas + placements -> neutral GridLayout DTO.
    # NOT an adapter; adds no Rule, mutates nothing, returns None when absent.
    # =====================================================================
    def get_grid_layout(self, grid_id: uuid.UUID) -> GridLayout | None:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return None  # C-REPO-NOTFOUND: not-found is None, not Result
        placements = sorted(
            self._placements.get_by_grid(grid_id),
            key=lambda p: p.placement_order,
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
            col_weights=tuple(grid.col_weights),
            row_weights=tuple(grid.row_weights),
            canvas_w=grid.canvas_size.width,
            canvas_h=grid.canvas_size.height,
            placements=views,
        )
