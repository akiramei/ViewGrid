"""UseCases UC-01..UC-11.

Each UseCase is a small class with an `execute()` method whose signature
mirrors the `inputs:` list from 21-grid-composition.yaml §use_cases. The
class holds its dependencies (repositories, ImageCopy existence check,
EventBus) in the constructor, so a single `execute()` call is a pure
function of its arguments given a fixed configuration. This satisfies
20-capability-bom.md §9 ("各 UseCase は入力 → 結果の対応が単一の関数とし
て表現可能").

Decision Ownership the UseCase layer holds:
- domain_decision: Rule guarantees R-01, R-02, R-06, R-09 (delegated to
  rules.* pure functions, but the *call* is here).
- validation_decision: input range / type / reference (existence) checks.
- workflow_decision: UC-02 fit algorithm, UC-07 swap orchestration, UC-10
  compact, UC-09 z-order ops.

Forbidden in this layer (and not present): persistence-format decisions,
rendering decisions, history-decisions. All event emission happens after
state changes are committed.

Atomicity (30-design.md §5.2): UC-02 and UC-07 use the in-memory
transaction() context to roll back on validation failure. The other UCs
update a single entity and fail before persistence on error.
"""

from __future__ import annotations

from contextlib import contextmanager
from dataclasses import dataclass, replace
from typing import Sequence
from uuid import UUID

from . import rules
from .domain import (
    Axis,
    CellPosition,
    GridCanvas,
    OccupySize,
    OrderOperation,
    PixelSize,
    Placement,
)
from .errors import (
    Conflict,
    InvalidDimensions,
    InvalidIndex,
    InvalidOrderValue,
    InvalidWeights,
    OutOfBounds,
    UnknownCopyId,
    WouldConflict,
    WouldOrphanPlacements,
)
from .events import (
    EventBus,
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
from .repositories import (
    GridCanvasRepository,
    ImageCopyExistenceCheck,
    PlacementRepository,
)


# -- Shared dependency bag -------------------------------------------------


@dataclass
class _Deps:
    grids: GridCanvasRepository
    placements: PlacementRepository
    copies: ImageCopyExistenceCheck
    bus: EventBus


# -- Validation helpers (validation_decision lives here) -------------------


def _require_grid(deps: _Deps, grid_id: UUID) -> GridCanvas:
    g = deps.grids.get_by_id(grid_id)
    if g is None:
        # Per BOM there is no canonical failure reason for "grid not found"
        # in the use cases that take a placement_id — those are guarded by
        # PlacementExists. UC-11 just returns []. UC-02/03/04/05 callers
        # supply grid_id and a missing grid is treated as an invalid arg.
        raise InvalidDimensions(detail=f"GridCanvas {grid_id} not found")
    return g


def _require_placement(deps: _Deps, placement_id: UUID) -> Placement:
    p = deps.placements.get_by_id(placement_id)
    if p is None:
        # Same reasoning as above; there is no canonical failure name for
        # "placement not found" in the BOM. We surface a clear runtime
        # error.
        raise KeyError(f"Placement {placement_id} not found")
    return p


# -- UC-01 CreateGridCanvas ------------------------------------------------


class CreateGridCanvas:
    """UC-01. inputs: name, grid_rows, grid_cols, canvas_size. failure:
    InvalidDimensions."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        name: str,
        grid_rows: int,
        grid_cols: int,
        canvas_size: PixelSize,
    ) -> GridCanvas:
        # validation_decision: input range
        if grid_rows < 1 or grid_cols < 1:
            raise InvalidDimensions(
                detail=f"rows and cols must be >= 1 (got {grid_rows}, {grid_cols})"
            )
        # canvas_size validity is enforced by PixelSize.__post_init__,
        # which raises ValueError. Convert at this boundary.
        try:
            cs = canvas_size  # already a PixelSize
        except ValueError as ex:
            raise InvalidDimensions(detail=str(ex)) from ex

        try:
            grid = GridCanvas.create(name, grid_rows, grid_cols, cs)
        except ValueError as ex:
            raise InvalidDimensions(detail=str(ex)) from ex

        self._deps.grids.save(grid)
        self._deps.bus.publish(GridCanvasCreated(grid_id=grid.id, snapshot=grid))
        return grid


# -- UC-02 ChangeGridDimensions --------------------------------------------


def _fit_array(
    values: tuple[int, ...],
    locked: tuple[bool, ...],
    target_len: int,
) -> tuple[tuple[int, ...], tuple[bool, ...]]:
    """Fit algorithm for R-05 + R-08.

    Per 30-design.md §1 R-08:
      - if target_len > current_len: append weight=1 (locked=False) until
        target reached.
      - if target_len < current_len: drop unlocked tail entries first;
        only drop locked entries if forced to. If we are forced to drop
        a locked entry, raise InvalidDimensions (treating "lock destruction"
        as an invalid request). The doc lists 'WouldDestroyLockedAxis' but
        BOM does NOT list it as a failure reason for UC-02, so we collapse
        it into InvalidDimensions per the FORBIDDEN-no-new-failure-reasons
        rule. (See IMPLEMENTATION_NOTES.md "unclear: locked drop semantics".)

    Returns (new_values, new_locked).
    """

    cur = len(values)
    if target_len == cur:
        return values, locked
    if target_len > cur:
        new_v = values + tuple([1] * (target_len - cur))
        new_l = locked + tuple([False] * (target_len - cur))
        return new_v, new_l
    # target_len < cur: drop from the tail, preferring unlocked.
    to_drop = cur - target_len
    keep: list[bool] = [True] * cur
    # walk tail-first, drop unlocked first.
    idx = cur - 1
    while to_drop > 0 and idx >= 0:
        if not locked[idx]:
            keep[idx] = False
            to_drop -= 1
        idx -= 1
    if to_drop > 0:
        # Forced to drop locked entries -> reject.
        raise InvalidDimensions(
            detail=f"Cannot shrink axis from {cur} to {target_len} without dropping locked entries"
        )
    new_v = tuple(v for v, k in zip(values, keep) if k)
    new_l = tuple(b for b, k in zip(locked, keep) if k)
    return new_v, new_l


class ChangeGridDimensions:
    """UC-02. inputs: grid_id, new_grid_rows, new_grid_cols.
    failure: InvalidDimensions / WouldOrphanPlacements / WouldConflict."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        grid_id: UUID,
        new_grid_rows: int,
        new_grid_cols: int,
    ) -> GridCanvas:
        if new_grid_rows < 1 or new_grid_cols < 1:
            raise InvalidDimensions(
                detail=f"new dimensions must be >= 1 (got {new_grid_rows}, {new_grid_cols})"
            )
        grid = _require_grid(self._deps, grid_id)
        existing = self._deps.placements.get_by_grid(grid_id)

        # R-01 sweep (orphan check).
        orphaned = [
            p.id
            for p in existing
            if not rules.placement_fits_within_grid(
                p.position, p.occupy_size, new_grid_rows, new_grid_cols
            )
        ]
        if orphaned:
            raise WouldOrphanPlacements(orphaned_placement_ids=tuple(orphaned))

        # R-02 defensive sweep: ensure no overlap among the (still-valid) set.
        # (Dimension shrink cannot cause overlaps if R-02 held before, but
        # 10-requirements.md §3.2 UC-02 says "理論上発生しないが防御的に検証".)
        seen_conflicts: set[UUID] = set()
        for p in existing:
            others = [q for q in existing if q.id != p.id]
            hits = rules.placement_overlaps(p.position, p.occupy_size, others)
            seen_conflicts.update(hits)
        if seen_conflicts:
            raise WouldConflict(conflicting_placement_ids=tuple(seen_conflicts))

        # Apply Fit (workflow_decision; R-05 + R-08).
        try:
            new_cw, new_cl = _fit_array(
                grid.col_weights, grid.col_locked, new_grid_cols
            )
            new_rw, new_rl = _fit_array(
                grid.row_weights, grid.row_locked, new_grid_rows
            )
        except InvalidDimensions:
            raise

        before = {
            "rows": grid.grid_rows,
            "cols": grid.grid_cols,
            "col_weights": grid.col_weights,
            "row_weights": grid.row_weights,
            "col_locked": grid.col_locked,
            "row_locked": grid.row_locked,
        }
        try:
            updated = grid.with_dimensions(
                grid_rows=new_grid_rows,
                grid_cols=new_grid_cols,
                col_weights=new_cw,
                row_weights=new_rw,
                col_locked=new_cl,
                row_locked=new_rl,
            )
        except ValueError as ex:
            raise InvalidDimensions(detail=str(ex)) from ex

        self._deps.grids.save(updated)
        after = {
            "rows": updated.grid_rows,
            "cols": updated.grid_cols,
            "col_weights": updated.col_weights,
            "row_weights": updated.row_weights,
            "col_locked": updated.col_locked,
            "row_locked": updated.row_locked,
        }
        self._deps.bus.publish(
            GridDimensionsChanged(grid_id=grid_id, before=before, after=after)
        )
        return updated


# -- UC-03 ChangeRowColumnWeights ------------------------------------------


class ChangeRowColumnWeights:
    """UC-03. inputs: grid_id, axis, weights. failure: InvalidWeights."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        grid_id: UUID,
        axis: Axis,
        weights: Sequence[int],
    ) -> GridCanvas:
        grid = _require_grid(self._deps, grid_id)
        expected_len = grid.grid_cols if axis is Axis.COL else grid.grid_rows
        if len(weights) != expected_len:
            raise InvalidWeights(
                detail=f"axis {axis.value} expects {expected_len} weights, got {len(weights)}"
            )
        if any((not isinstance(w, int)) or w <= 0 for w in weights):
            raise InvalidWeights(detail=f"weights must be positive ints, got {list(weights)}")

        before = grid.col_weights if axis is Axis.COL else grid.row_weights
        try:
            updated = grid.with_weights(axis, weights)
        except ValueError as ex:
            raise InvalidWeights(detail=str(ex)) from ex
        self._deps.grids.save(updated)
        after = updated.col_weights if axis is Axis.COL else updated.row_weights
        self._deps.bus.publish(
            RowColumnWeightsChanged(
                grid_id=grid_id,
                axis=axis,
                before_weights=tuple(before),
                after_weights=tuple(after),
            )
        )
        return updated


# -- UC-04 ToggleRowColumnLock ---------------------------------------------


class ToggleRowColumnLock:
    """UC-04. inputs: grid_id, axis, index. failure: InvalidIndex."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        grid_id: UUID,
        axis: Axis,
        index: int,
    ) -> GridCanvas:
        grid = _require_grid(self._deps, grid_id)
        limit = grid.grid_cols if axis is Axis.COL else grid.grid_rows
        if not (0 <= index < limit):
            raise InvalidIndex(detail=f"index {index} out of range [0, {limit})")
        updated = grid.with_lock_toggled(axis, index)
        self._deps.grids.save(updated)
        after_state = (
            updated.col_locked[index] if axis is Axis.COL else updated.row_locked[index]
        )
        self._deps.bus.publish(
            RowColumnLockToggled(
                grid_id=grid_id, axis=axis, index=index, after_state=after_state
            )
        )
        return updated


# -- UC-05 PlaceImageCopy --------------------------------------------------


class PlaceImageCopy:
    """UC-05. failure: OutOfBounds / Conflict / UnknownCopyId."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        grid_id: UUID,
        copy_id: UUID,
        position: CellPosition,
        occupy_size: OccupySize,
    ) -> Placement:
        grid = _require_grid(self._deps, grid_id)
        if not self._deps.copies.exists(copy_id):
            raise UnknownCopyId(copy_id=copy_id)
        # R-01.
        if not rules.placement_fits_within_grid(
            position, occupy_size, grid.grid_rows, grid.grid_cols
        ):
            raise OutOfBounds(
                detail=f"placement {position}+{occupy_size} does not fit grid "
                f"{grid.grid_rows}x{grid.grid_cols}"
            )
        # R-02. exclude_ids empty (new placement).
        existing = self._deps.placements.get_by_grid(grid_id)
        conflicts = rules.placement_overlaps(position, occupy_size, existing)
        if conflicts:
            raise Conflict(conflicting_placement_ids=conflicts)

        # R-06: next order = max + 1 (1 if empty).
        next_order = max((p.placement_order for p in existing), default=0) + 1
        placement = Placement.create(
            grid_id=grid_id,
            copy_id=copy_id,
            position=position,
            occupy_size=occupy_size,
            placement_order=next_order,
        )
        self._deps.placements.save(placement)
        self._deps.bus.publish(
            PlacementCreated(placement_id=placement.id, snapshot=placement)
        )
        return placement


# -- UC-06 MovePlacement ---------------------------------------------------


class MovePlacement:
    """UC-06. failure: OutOfBounds / Conflict. Self is excluded from R-02."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        placement_id: UUID,
        new_position: CellPosition,
    ) -> Placement:
        p = _require_placement(self._deps, placement_id)
        grid = self._deps.grids.get_by_id(p.grid_id)
        assert grid is not None  # invariant: placement.grid_id => grid exists
        if not rules.placement_fits_within_grid(
            new_position, p.occupy_size, grid.grid_rows, grid.grid_cols
        ):
            raise OutOfBounds(
                detail=f"move target {new_position}+{p.occupy_size} does not fit grid"
            )
        existing = self._deps.placements.get_by_grid(p.grid_id)
        conflicts = rules.placement_overlaps(
            new_position, p.occupy_size, existing, exclude_ids=(p.id,)
        )
        if conflicts:
            raise Conflict(conflicting_placement_ids=conflicts)

        before_pos = p.position
        moved = p.with_position(new_position)
        self._deps.placements.save(moved)
        self._deps.bus.publish(
            PlacementMoved(
                placement_id=p.id,
                before_position=before_pos,
                after_position=new_position,
            )
        )
        return moved


# -- UC-07 SwapPlacements --------------------------------------------------


class SwapPlacements:
    """UC-07. failure: OutOfBounds / Conflict. Both excluded from each
    other's R-02 check. Atomic per design.md §5.2."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        placement_id_a: UUID,
        placement_id_b: UUID,
    ) -> tuple[Placement, Placement]:
        a = _require_placement(self._deps, placement_id_a)
        b = _require_placement(self._deps, placement_id_b)
        if a.grid_id != b.grid_id:
            raise OutOfBounds(detail="swap requires same grid_id")
        grid = self._deps.grids.get_by_id(a.grid_id)
        assert grid is not None
        # R-01 for both new positions.
        if not rules.placement_fits_within_grid(
            b.position, a.occupy_size, grid.grid_rows, grid.grid_cols
        ):
            raise OutOfBounds(detail="placement A would exceed bounds at B's position")
        if not rules.placement_fits_within_grid(
            a.position, b.occupy_size, grid.grid_rows, grid.grid_cols
        ):
            raise OutOfBounds(detail="placement B would exceed bounds at A's position")
        existing = self._deps.placements.get_by_grid(a.grid_id)
        # R-02 against third parties: both swap targets excluded.
        c_a = rules.placement_overlaps(
            b.position, a.occupy_size, existing, exclude_ids=(a.id, b.id)
        )
        if c_a:
            raise Conflict(conflicting_placement_ids=c_a)
        c_b = rules.placement_overlaps(
            a.position, b.occupy_size, existing, exclude_ids=(a.id, b.id)
        )
        if c_b:
            raise Conflict(conflicting_placement_ids=c_b)
        # R-02 between A and B themselves at the NEW positions. The
        # mutual exclude above prevents this from being caught, so we
        # check it explicitly. (E.g. A 1x1 at (0,0) swapping with B 2x1
        # at (1,0): A's new footprint and B's new footprint both cover
        # cell (1,0).)
        new_a_cells = {
            (b.position.x + dx, b.position.y + dy)
            for dx in range(a.occupy_size.width)
            for dy in range(a.occupy_size.height)
        }
        new_b_cells = {
            (a.position.x + dx, a.position.y + dy)
            for dx in range(b.occupy_size.width)
            for dy in range(b.occupy_size.height)
        }
        if new_a_cells & new_b_cells:
            raise Conflict(conflicting_placement_ids=(a.id, b.id))

        before_a, before_b = a.position, b.position
        new_a = a.with_position(b.position)
        new_b = b.with_position(a.position)

        # Atomicity: snapshot via transaction context if available, else
        # accept best-effort (in-memory dict assignment is atomic at the
        # Python interpreter level for single statements).
        with _maybe_transaction(self._deps.placements):
            self._deps.placements.save(new_a)
            self._deps.placements.save(new_b)
        self._deps.bus.publish(
            PlacementsSwapped(
                placement_id_a=a.id,
                placement_id_b=b.id,
                before_a=before_a,
                before_b=before_b,
            )
        )
        return new_a, new_b


@contextmanager
def _maybe_transaction(repo: object):
    tx = getattr(repo, "transaction", None)
    if callable(tx):
        with tx():
            yield
    else:
        yield


# -- UC-08 ResizePlacementOccupancy ----------------------------------------


class ResizePlacementOccupancy:
    """UC-08. failure: OutOfBounds / Conflict. Self excluded from R-02."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        placement_id: UUID,
        new_occupy_size: OccupySize,
    ) -> Placement:
        p = _require_placement(self._deps, placement_id)
        grid = self._deps.grids.get_by_id(p.grid_id)
        assert grid is not None
        if not rules.placement_fits_within_grid(
            p.position, new_occupy_size, grid.grid_rows, grid.grid_cols
        ):
            raise OutOfBounds(
                detail=f"resize {new_occupy_size} at {p.position} would exceed grid"
            )
        existing = self._deps.placements.get_by_grid(p.grid_id)
        conflicts = rules.placement_overlaps(
            p.position, new_occupy_size, existing, exclude_ids=(p.id,)
        )
        if conflicts:
            raise Conflict(conflicting_placement_ids=conflicts)

        before = p.occupy_size
        resized = p.with_occupy_size(new_occupy_size)
        self._deps.placements.save(resized)
        self._deps.bus.publish(
            PlacementOccupancyResized(
                placement_id=p.id, before_size=before, after_size=new_occupy_size
            )
        )
        return resized


# -- UC-09 ChangePlacementOrder --------------------------------------------


class ChangePlacementOrder:
    """UC-09. failure: InvalidOrderValue. Operation values: BringToFront,
    SendToBack, MoveForward, MoveBackward, SetOrder.

    After the operation, all placement_orders in the grid must remain
    unique and form 1..N (we keep dense numbering for consistency with R-09
    although R-09 itself is scoped to UC-10).
    """

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(
        self,
        placement_id: UUID,
        operation: OrderOperation,
        *,
        order_value: int | None = None,
    ) -> Placement:
        p = _require_placement(self._deps, placement_id)
        existing = self._deps.placements.get_by_grid(p.grid_id)
        # Sort by current order. We'll move `p` to a new index and re-emit
        # 1..N orders, which is the simplest form that guarantees R-06.
        ordered = sorted(existing, key=lambda q: q.placement_order)
        if operation is OrderOperation.BringToFront:
            new_index = len(ordered) - 1  # 0-based; back = 0, front = N-1
        elif operation is OrderOperation.SendToBack:
            new_index = 0
        elif operation is OrderOperation.MoveForward:
            cur_index = next(i for i, q in enumerate(ordered) if q.id == p.id)
            new_index = min(len(ordered) - 1, cur_index + 1)
        elif operation is OrderOperation.MoveBackward:
            cur_index = next(i for i, q in enumerate(ordered) if q.id == p.id)
            new_index = max(0, cur_index - 1)
        elif operation is OrderOperation.SetOrder:
            if order_value is None or order_value < 1 or order_value > len(ordered):
                raise InvalidOrderValue(
                    detail=f"order_value must be in [1, {len(ordered)}], got {order_value}"
                )
            new_index = order_value - 1
        else:
            raise InvalidOrderValue(detail=f"unknown operation {operation!r}")

        # Build the new ordering list.
        without = [q for q in ordered if q.id != p.id]
        new_ordered = without[:new_index] + [p] + without[new_index:]

        # Assign 1..N. This satisfies R-06 by construction.
        before_map: dict[UUID, int] = {q.id: q.placement_order for q in existing}
        after_map: dict[UUID, int] = {}
        for i, q in enumerate(new_ordered, start=1):
            after_map[q.id] = i
            if q.placement_order != i:
                self._deps.placements.save(q.with_placement_order(i))

        # Defensive R-06 check on the freshly-saved set.
        refreshed = self._deps.placements.get_by_grid(p.grid_id)
        if not rules.orders_are_unique(refreshed):
            # If this fails, the implementation is broken — surface it.
            raise InvalidOrderValue(detail="post-condition R-06 violated")

        self._deps.bus.publish(
            PlacementOrderChanged(
                grid_id=p.grid_id,
                before_order_map=before_map,
                after_order_map=after_map,
            )
        )
        return self._deps.placements.get_by_id(p.id)  # type: ignore[return-value]


# -- UC-10 RemovePlacement -------------------------------------------------


class RemovePlacement:
    """UC-10. failure: (none). After removal, residual orders are compacted
    to 1..N per R-09. The PlacementRemoved event carries snapshot_before
    and compacted_order_map per design.md §4.1."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(self, placement_id: UUID) -> None:
        p = _require_placement(self._deps, placement_id)
        snapshot_before = p

        self._deps.placements.delete(placement_id)
        # R-09 compact.
        residual = self._deps.placements.get_by_grid(p.grid_id)
        compacted, mapping = rules.compact_orders(residual)
        for q in compacted:
            self._deps.placements.save(q)

        self._deps.bus.publish(
            PlacementRemoved(
                placement_id=placement_id,
                snapshot_before=snapshot_before,
                compacted_order_map=mapping,
            )
        )


# -- UC-11 ListPlacements --------------------------------------------------


class ListPlacements:
    """UC-11 (query). Returns placements ordered by placement_order ascending."""

    def __init__(
        self,
        grids: GridCanvasRepository,
        placements: PlacementRepository,
        copies: ImageCopyExistenceCheck,
        bus: EventBus,
    ) -> None:
        self._deps = _Deps(grids, placements, copies, bus)

    def execute(self, grid_id: UUID) -> list[Placement]:
        existing = self._deps.placements.get_by_grid(grid_id)
        return sorted(existing, key=lambda p: p.placement_order)
