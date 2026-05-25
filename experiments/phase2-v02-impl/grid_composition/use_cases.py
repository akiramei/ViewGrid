"""UseCases (UC-01 .. UC-11).

Every UseCase is a method that returns ``Result[..., Failure]``. Domain
failure values (per ``canonical_failure_reasons``) are returned, not
raised. Programmer errors (wrong type at the boundary) still raise.

Per Decision Ownership table:
- ``domain_decision`` (R-01, R-02) is performed via :mod:`rules` predicates.
- ``workflow_decision`` (UC-07 post-swap intersection, UC-02 fit-adjust,
  UC-10 order compaction, UC-09 order semantics) is owned here.
- ``persistence_decision`` is delegated to repositories.
- ``rendering_decision`` / ``history_decision`` are out of scope.

R-07 immutability:
    ``Placement`` is frozen; "modification" is expressed by constructing a
    new Placement via ``dataclasses.replace`` and saving it.
"""

from __future__ import annotations

from dataclasses import dataclass, replace
from typing import Dict, Generic, List, Optional, TypeVar, Union

from .entities import GridCanvas, Placement
from .events import (
    EventBus,
    GridCanvasCreated,
    GridDimensionsChanged,
    NullBus,
    PlacementCreated,
    PlacementMoved,
    PlacementOccupancyResized,
    PlacementOrderChanged,
    PlacementRemoved,
    PlacementsSwapped,
    RowColumnLockToggled,
    RowColumnWeightsChanged,
)
from .failures import (
    Conflict,
    Failure,
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
from .identity import Id, new_id
from .repositories import (
    GridCanvasRepository,
    ImageCopyExistenceCheck,
    PlacementRepository,
)
from .rules import find_conflicts, fits_within_grid, occupied_cells
from .value_objects import Axis, CellPosition, OccupySize, OrderOperation, PixelSize

T = TypeVar("T")


# ---------------------------------------------------------------------------
# Result type
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Ok(Generic[T]):
    value: T

    @property
    def is_ok(self) -> bool:
        return True

    @property
    def is_err(self) -> bool:
        return False


@dataclass(frozen=True)
class Err:
    failure: Failure

    @property
    def is_ok(self) -> bool:
        return False

    @property
    def is_err(self) -> bool:
        return True


Result = Union[Ok[T], Err]


# ---------------------------------------------------------------------------
# UseCase coordinator
# ---------------------------------------------------------------------------


class GridCompositionUseCases:
    """Coordinator exposing the 11 UseCases as plain methods.

    Tests interact with this class directly (UI/CLI layer is out of scope).
    All side effects go through the injected repositories + event bus.
    """

    def __init__(
        self,
        grid_repo: GridCanvasRepository,
        placement_repo: PlacementRepository,
        image_copy_check: ImageCopyExistenceCheck,
        event_bus: Optional[EventBus] = None,
    ) -> None:
        self._grids = grid_repo
        self._placements = placement_repo
        self._copies = image_copy_check
        self._bus = event_bus or NullBus()

    # ------------------------------------------------------------------ UC-01
    def create_grid_canvas(
        self,
        *,
        name: str,
        grid_rows: int,
        grid_cols: int,
        canvas_size: PixelSize,
    ) -> Result[GridCanvas]:
        if not isinstance(grid_rows, int) or grid_rows < 1:
            return Err(InvalidDimensions(detail=f"grid_rows must be >= 1, got {grid_rows!r}"))
        if not isinstance(grid_cols, int) or grid_cols < 1:
            return Err(InvalidDimensions(detail=f"grid_cols must be >= 1, got {grid_cols!r}"))
        # PixelSize already validates >0 in its constructor.

        grid = GridCanvas.create(
            id=new_id(),
            name=name,
            grid_rows=grid_rows,
            grid_cols=grid_cols,
            canvas_size=canvas_size,
        )
        self._grids.save(grid)
        self._bus.publish(GridCanvasCreated(grid_id=grid.id, snapshot=grid))
        return Ok(grid)

    # ------------------------------------------------------------------ UC-02
    def change_grid_dimensions(
        self,
        *,
        grid_id: Id,
        new_grid_rows: int,
        new_grid_cols: int,
    ) -> Result[GridCanvas]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        if not isinstance(new_grid_rows, int) or new_grid_rows < 1:
            return Err(InvalidDimensions(detail=f"new_grid_rows must be >= 1, got {new_grid_rows!r}"))
        if not isinstance(new_grid_cols, int) or new_grid_cols < 1:
            return Err(InvalidDimensions(detail=f"new_grid_cols must be >= 1, got {new_grid_cols!r}"))

        # Build the prospective new grid value (with adjusted weights/locks).
        new_col_weights = self._fit_weights(grid.col_weights, grid.col_locked, new_grid_cols)
        new_row_weights = self._fit_weights(grid.row_weights, grid.row_locked, new_grid_rows)
        new_col_locked = self._fit_locks(grid.col_locked, new_grid_cols)
        new_row_locked = self._fit_locks(grid.row_locked, new_grid_rows)

        try:
            prospective = replace(
                grid,
                grid_rows=new_grid_rows,
                grid_cols=new_grid_cols,
                col_weights=new_col_weights,
                row_weights=new_row_weights,
                col_locked=new_col_locked,
                row_locked=new_row_locked,
            )
        except ValueError as exc:
            # Defensive: shouldn't happen because _fit_* preserves lengths.
            return Err(InvalidDimensions(detail=str(exc)))

        # R-01: orphans = placements that no longer fit
        existing = self._placements.get_by_grid(grid_id)
        orphans = [
            p.id
            for p in existing
            if not fits_within_grid(prospective, p.position, p.occupy_size)
        ]
        if orphans:
            return Err(WouldOrphanPlacements(orphaned_placement_ids=tuple(orphans)))

        # R-02: defensive check of all existing placements after dimension change.
        conflicts = self._detect_internal_conflicts(existing)
        if conflicts:
            return Err(WouldConflict(conflicting_placement_ids=tuple(conflicts)))

        after = prospective.with_updated_at()
        self._grids.save(after)
        self._bus.publish(
            GridDimensionsChanged(grid_id=grid_id, before=grid, after=after)
        )
        return Ok(after)

    # ------------------------------------------------------------------ UC-03
    def change_row_column_weights(
        self,
        *,
        grid_id: Id,
        axis: Axis,
        weights: List[int] | tuple,
    ) -> Result[GridCanvas]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        weights_tuple = tuple(weights)
        expected_len = grid.grid_cols if axis == Axis.COL else grid.grid_rows
        if len(weights_tuple) != expected_len:
            return Err(
                InvalidWeights(
                    detail=f"weights length {len(weights_tuple)} != axis dim {expected_len}"
                )
            )
        for w in weights_tuple:
            if not isinstance(w, int) or isinstance(w, bool) or w <= 0:
                return Err(InvalidWeights(detail=f"weight must be positive int, got {w!r}"))

        if axis == Axis.COL:
            before = grid.col_weights
            new_grid = replace(grid, col_weights=weights_tuple).with_updated_at()
        else:
            before = grid.row_weights
            new_grid = replace(grid, row_weights=weights_tuple).with_updated_at()

        self._grids.save(new_grid)
        self._bus.publish(
            RowColumnWeightsChanged(
                grid_id=grid_id, axis=axis, before_weights=before, after_weights=weights_tuple
            )
        )
        return Ok(new_grid)

    # ------------------------------------------------------------------ UC-04
    def toggle_row_column_lock(
        self,
        *,
        grid_id: Id,
        axis: Axis,
        index: int,
    ) -> Result[GridCanvas]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        max_index = grid.grid_cols if axis == Axis.COL else grid.grid_rows
        if not isinstance(index, int) or isinstance(index, bool) or index < 0 or index >= max_index:
            return Err(InvalidIndex(axis=axis, index=index))

        if axis == Axis.COL:
            new_locked = list(grid.col_locked)
            new_locked[index] = not new_locked[index]
            after_state = new_locked[index]
            new_grid = replace(grid, col_locked=tuple(new_locked)).with_updated_at()
        else:
            new_locked = list(grid.row_locked)
            new_locked[index] = not new_locked[index]
            after_state = new_locked[index]
            new_grid = replace(grid, row_locked=tuple(new_locked)).with_updated_at()

        self._grids.save(new_grid)
        self._bus.publish(
            RowColumnLockToggled(
                grid_id=grid_id, axis=axis, index=index, after_state=after_state
            )
        )
        return Ok(new_grid)

    # ------------------------------------------------------------------ UC-05
    def place_image_copy(
        self,
        *,
        grid_id: Id,
        copy_id: Id,
        position: CellPosition,
        occupy_size: OccupySize,
    ) -> Result[Placement]:
        grid = self._grids.get_by_id(grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=grid_id))

        if not self._copies.exists(copy_id):
            return Err(UnknownCopyId(copy_id=copy_id))

        if not fits_within_grid(grid, position, occupy_size):
            return Err(OutOfBounds(attempted_position=position, occupy_size=occupy_size))

        existing = self._placements.get_by_grid(grid_id)
        cand_cells = occupied_cells(position, occupy_size)
        conflicts = find_conflicts(cand_cells, existing)
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=conflicts))

        # R-06 — order is max existing + 1, or 1 if empty.
        max_order = max((p.placement_order for p in existing), default=0)
        placement = Placement(
            id=new_id(),
            grid_id=grid_id,
            copy_id=copy_id,
            position=position,
            occupy_size=occupy_size,
            placement_order=max_order + 1,
        )
        self._placements.save(placement)
        self._bus.publish(PlacementCreated(placement_id=placement.id, snapshot=placement))
        return Ok(placement)

    # ------------------------------------------------------------------ UC-06
    def move_placement(
        self,
        *,
        placement_id: Id,
        new_position: CellPosition,
    ) -> Result[Placement]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))

        grid = self._grids.get_by_id(placement.grid_id)
        if grid is None:
            # Defensive — grid disappearance is an invariant violation in our
            # model, but reporting it consistently keeps the contract clean.
            return Err(NotFound(entity_kind="GridCanvas", entity_id=placement.grid_id))

        if not fits_within_grid(grid, new_position, placement.occupy_size):
            return Err(
                OutOfBounds(attempted_position=new_position, occupy_size=placement.occupy_size)
            )

        existing = self._placements.get_by_grid(grid.id)
        cand_cells = occupied_cells(new_position, placement.occupy_size)
        conflicts = find_conflicts(cand_cells, existing, exclude_ids=[placement.id])
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=conflicts))

        moved = replace(placement, position=new_position)
        self._placements.save(moved)
        self._bus.publish(
            PlacementMoved(
                placement_id=placement.id,
                before_position=placement.position,
                after_position=new_position,
            )
        )
        return Ok(moved)

    # ------------------------------------------------------------------ UC-07
    def swap_placements(
        self,
        *,
        placement_id_a: Id,
        placement_id_b: Id,
    ) -> Result[tuple[Placement, Placement]]:
        # (i) both must exist; return first-missing per BOM §2.1
        a = self._placements.get_by_id(placement_id_a)
        if a is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id_a))
        b = self._placements.get_by_id(placement_id_b)
        if b is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id_b))

        # Implementation requires both belong to the same grid (cross-grid
        # swap not defined in the docs — treated as out-of-scope; we return
        # Conflict on the assumption that overlap can never resolve).
        if a.grid_id != b.grid_id:
            # MUST_DECIDE_AND_DOCUMENT: cross-grid swap unspecified.
            # We refuse via Conflict citing both ids to keep the failure
            # reason set canonical.
            return Err(Conflict(conflicting_placement_ids=(a.id, b.id)))

        grid = self._grids.get_by_id(a.grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=a.grid_id))

        new_pos_a = b.position
        new_pos_b = a.position

        # (ii) R-01 — both new positions fit
        if not fits_within_grid(grid, new_pos_a, a.occupy_size):
            return Err(OutOfBounds(attempted_position=new_pos_a, occupy_size=a.occupy_size))
        if not fits_within_grid(grid, new_pos_b, b.occupy_size):
            return Err(OutOfBounds(attempted_position=new_pos_b, occupy_size=b.occupy_size))

        existing = self._placements.get_by_grid(grid.id)

        # (iii) R-02 — candidate vs. existing (excluding both swap participants)
        cells_a = occupied_cells(new_pos_a, a.occupy_size)
        cells_b = occupied_cells(new_pos_b, b.occupy_size)
        conflicts_a = find_conflicts(cells_a, existing, exclude_ids=[a.id, b.id])
        conflicts_b = find_conflicts(cells_b, existing, exclude_ids=[a.id, b.id])
        if conflicts_a or conflicts_b:
            combined = tuple(dict.fromkeys((*conflicts_a, *conflicts_b)))
            return Err(Conflict(conflicting_placement_ids=combined))

        # (iv) workflow_decision: A's new cells vs. B's new cells must NOT
        # intersect. This is NOT a duplicate of R-02 — it lives only in
        # UC-07 per 30-design §1 R-02 note and §2.2 UC-07. See W-3.
        if cells_a & cells_b:
            return Err(Conflict(conflicting_placement_ids=(a.id, b.id)))

        # (v) atomically update both. (Single thread, in-memory; we save in
        # a defined order with no partial-failure surface.)
        new_a = replace(a, position=new_pos_a)
        new_b = replace(b, position=new_pos_b)
        self._placements.save(new_a)
        self._placements.save(new_b)

        # (vi) emit
        self._bus.publish(
            PlacementsSwapped(
                placement_id_a=a.id,
                placement_id_b=b.id,
                before_a=a.position,
                before_b=b.position,
            )
        )
        return Ok((new_a, new_b))

    # ------------------------------------------------------------------ UC-08
    def resize_placement_occupancy(
        self,
        *,
        placement_id: Id,
        new_occupy_size: OccupySize,
    ) -> Result[Placement]:
        placement = self._placements.get_by_id(placement_id)
        if placement is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))

        grid = self._grids.get_by_id(placement.grid_id)
        if grid is None:
            return Err(NotFound(entity_kind="GridCanvas", entity_id=placement.grid_id))

        if not fits_within_grid(grid, placement.position, new_occupy_size):
            return Err(
                OutOfBounds(
                    attempted_position=placement.position, occupy_size=new_occupy_size
                )
            )

        existing = self._placements.get_by_grid(grid.id)
        cand_cells = occupied_cells(placement.position, new_occupy_size)
        conflicts = find_conflicts(cand_cells, existing, exclude_ids=[placement.id])
        if conflicts:
            return Err(Conflict(conflicting_placement_ids=conflicts))

        resized = replace(placement, occupy_size=new_occupy_size)
        self._placements.save(resized)
        self._bus.publish(
            PlacementOccupancyResized(
                placement_id=placement.id,
                before_size=placement.occupy_size,
                after_size=new_occupy_size,
            )
        )
        return Ok(resized)

    # ------------------------------------------------------------------ UC-09
    def change_placement_order(
        self,
        *,
        placement_id: Id,
        operation: OrderOperation,
        order_value: Optional[int] = None,
    ) -> Result[Placement]:
        target = self._placements.get_by_id(placement_id)
        if target is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))

        siblings = self._placements.get_by_grid(target.grid_id)
        # Sort by current order for deterministic positions.
        siblings_sorted = sorted(siblings, key=lambda p: p.placement_order)
        n = len(siblings_sorted)

        # order_value channel rules (UC-09 input_notes + canonical failure):
        if operation == OrderOperation.SET_ORDER:
            if order_value is None:
                return Err(
                    InvalidOrderValue(
                        detail="order_value required for SetOrder",
                        attempted_value=None,
                    )
                )
            if not isinstance(order_value, int) or isinstance(order_value, bool):
                return Err(
                    InvalidOrderValue(
                        detail=f"order_value must be int, got {order_value!r}",
                        attempted_value=None,
                    )
                )
            if order_value < 1 or order_value > n:
                return Err(
                    InvalidOrderValue(
                        detail=f"order_value out of [1..{n}]",
                        attempted_value=order_value,
                    )
                )
        else:
            if order_value is not None:
                return Err(
                    InvalidOrderValue(
                        detail=f"order_value must be None for operation {operation.value}",
                        attempted_value=order_value,
                    )
                )

        # Compute the desired new index (0-based) of the target after the op.
        current_index = next(
            i for i, p in enumerate(siblings_sorted) if p.id == target.id
        )
        if operation == OrderOperation.BRING_TO_FRONT:
            new_index = n - 1
        elif operation == OrderOperation.SEND_TO_BACK:
            new_index = 0
        elif operation == OrderOperation.MOVE_FORWARD:
            new_index = min(current_index + 1, n - 1)
        elif operation == OrderOperation.MOVE_BACKWARD:
            new_index = max(current_index - 1, 0)
        else:  # SET_ORDER
            assert order_value is not None  # already validated
            new_index = order_value - 1  # 1-based -> 0-based

        # Rebuild order list.
        before_order_map: Dict[Id, int] = {p.id: p.placement_order for p in siblings_sorted}
        reordered = [p for p in siblings_sorted if p.id != target.id]
        reordered.insert(new_index, target)

        after_order_map: Dict[Id, int] = {}
        for i, p in enumerate(reordered):
            new_order = i + 1
            after_order_map[p.id] = new_order
            if p.placement_order != new_order:
                self._placements.save(replace(p, placement_order=new_order))

        # R-06 invariant assertion: orders are unique 1..N (constructive).
        assert sorted(after_order_map.values()) == list(range(1, n + 1)), \
            "R-06 invariant broken by UC-09"

        self._bus.publish(
            PlacementOrderChanged(
                grid_id=target.grid_id,
                before_order_map=before_order_map,
                after_order_map=after_order_map,
            )
        )
        # Return the updated target.
        return Ok(self._placements.get_by_id(target.id))  # type: ignore[return-value]

    # ------------------------------------------------------------------ UC-10
    def remove_placement(
        self,
        *,
        placement_id: Id,
    ) -> Result[Placement]:
        target = self._placements.get_by_id(placement_id)
        if target is None:
            return Err(NotFound(entity_kind="Placement", entity_id=placement_id))

        siblings = self._placements.get_by_grid(target.grid_id)
        # Remove target, compact remaining (R-09).
        self._placements.delete(target.id)
        remaining = [p for p in siblings if p.id != target.id]
        remaining_sorted = sorted(remaining, key=lambda p: p.placement_order)
        compacted: Dict[Id, int] = {}
        for i, p in enumerate(remaining_sorted):
            new_order = i + 1
            compacted[p.id] = new_order
            if p.placement_order != new_order:
                self._placements.save(replace(p, placement_order=new_order))

        self._bus.publish(
            PlacementRemoved(
                placement_id=target.id,
                snapshot_before=target,
                compacted_order_map=compacted,
            )
        )
        return Ok(target)

    # ------------------------------------------------------------------ UC-11
    def list_placements(self, *, grid_id: Id) -> Result[List[Placement]]:
        # Per UC-11 notes: never fails — empty list when grid absent.
        placements = self._placements.get_by_grid(grid_id)
        ordered = sorted(placements, key=lambda p: p.placement_order)
        return Ok(ordered)

    # =================================================================
    # Internal helpers (workflow_decision belongs here)
    # =================================================================

    @staticmethod
    def _fit_weights(
        weights: tuple[int, ...], locked: tuple[bool, ...], target_dim: int
    ) -> tuple[int, ...]:
        """UC-02 Fit-adjust per R-08 + R-05.

        - If target == current: unchanged
        - Expand: append weight=1 entries
        - Shrink: drop trailing unlocked entries first; only drop a locked
          entry as a last resort. The locked-removal case is reported by
          UC-02 indirectly (currently treated as deterministic shrink — we
          choose to never raise here; locked-remove cases would surface
          as WouldOrphanPlacements / WouldConflict if they manifest, and
          v0.2 docs say "WouldDestroyLockedAxis as failure" is acceptable
          but not required. MUST_DECIDE_AND_DOCUMENT: we silently shrink.)
        """
        current = len(weights)
        if target_dim == current:
            return weights
        if target_dim > current:
            return weights + tuple([1] * (target_dim - current))
        # shrink
        # Indices of unlocked entries (drop priority).
        kept = list(weights)
        kept_locked = list(locked)
        to_drop = current - target_dim
        # Drop unlocked from the tail first.
        idx = len(kept) - 1
        while to_drop > 0 and idx >= 0:
            if not kept_locked[idx]:
                kept.pop(idx)
                kept_locked.pop(idx)
                to_drop -= 1
            idx -= 1
        # If still need to drop, peel locked from tail (best-effort).
        while to_drop > 0 and kept:
            kept.pop()
            kept_locked.pop()
            to_drop -= 1
        return tuple(kept)

    @staticmethod
    def _fit_locks(locked: tuple[bool, ...], target_dim: int) -> tuple[bool, ...]:
        current = len(locked)
        if target_dim == current:
            return locked
        if target_dim > current:
            return locked + tuple([False] * (target_dim - current))
        # shrink: keep first ``target_dim`` plus any locked we couldn't drop.
        # Use the same rule as _fit_weights so they stay in lock-step.
        kept = list(locked)
        to_drop = current - target_dim
        idx = len(kept) - 1
        while to_drop > 0 and idx >= 0:
            if not kept[idx]:
                kept.pop(idx)
                to_drop -= 1
            idx -= 1
        while to_drop > 0 and kept:
            kept.pop()
            to_drop -= 1
        return tuple(kept)

    @staticmethod
    def _detect_internal_conflicts(placements: list[Placement]) -> list[Id]:
        """Defensive — should not happen if R-02 was preserved by every
        prior UseCase. Reports IDs participating in at least one overlap."""
        bad: set[Id] = set()
        for i, a in enumerate(placements):
            for b in placements[i + 1 :]:
                if a.occupied_cells() & b.occupied_cells():
                    bad.add(a.id)
                    bad.add(b.id)
        return sorted(bad, key=str)
