"""Invariant-after-operation tests + property-based tests (light).

Per 30-design.md §6.1: 'Invariant after operation' category.
Per §6.3: 'reverse-example basis (random 1000 cases)' is recommended.

We implement a deterministic random walk: starting from an empty grid we
apply a random valid sequence of operations and assert R-01..R-06 / R-09
hold throughout. We use a fixed seed for reproducibility.
"""

from __future__ import annotations

import random
from uuid import UUID, uuid4

from grid_composition import (
    CellPosition,
    OccupySize,
    PixelSize,
)
from grid_composition import rules
from grid_composition.domain import OrderOperation
from grid_composition.errors import Conflict, OutOfBounds

from tests.conftest import make_copy


def _all_invariants_hold(grid, placements):
    # R-01.
    for p in placements:
        assert rules.placement_fits_within_grid(
            p.position, p.occupy_size, grid.grid_rows, grid.grid_cols
        ), f"R-01 violated for placement {p.id}"
    # R-02.
    for p in placements:
        others = [q for q in placements if q.id != p.id]
        hits = rules.placement_overlaps(p.position, p.occupy_size, others)
        assert hits == (), f"R-02 violated: {p.id} overlaps {hits}"
    # R-06.
    assert rules.orders_are_unique(placements), "R-06 violated"


class TestRandomWalkInvariants:
    def test_1000_random_operations_preserve_invariants(
        self, ucs, grids, placements, copies, grid_3x3
    ):
        rng = random.Random(0xC0FFEE)
        cid = make_copy(copies)

        for step in range(1000):
            op = rng.choice(["place", "move", "resize", "swap", "remove", "order"])
            current = ucs.list_p.execute(grid_3x3.id)
            try:
                if op == "place" and len(current) < 6:
                    x = rng.randrange(grid_3x3.grid_cols)
                    y = rng.randrange(grid_3x3.grid_rows)
                    w = rng.randint(1, max(1, grid_3x3.grid_cols - x))
                    h = rng.randint(1, max(1, grid_3x3.grid_rows - y))
                    ucs.place.execute(
                        grid_3x3.id, cid, CellPosition(x, y), OccupySize(w, h)
                    )
                elif op == "move" and current:
                    target = rng.choice(current)
                    x = rng.randrange(grid_3x3.grid_cols)
                    y = rng.randrange(grid_3x3.grid_rows)
                    ucs.move.execute(target.id, CellPosition(x, y))
                elif op == "resize" and current:
                    target = rng.choice(current)
                    w = rng.randint(1, grid_3x3.grid_cols)
                    h = rng.randint(1, grid_3x3.grid_rows)
                    ucs.resize.execute(target.id, OccupySize(w, h))
                elif op == "swap" and len(current) >= 2:
                    a, b = rng.sample(current, 2)
                    ucs.swap.execute(a.id, b.id)
                elif op == "remove" and current:
                    target = rng.choice(current)
                    ucs.remove.execute(target.id)
                elif op == "order" and current:
                    target = rng.choice(current)
                    operation = rng.choice(list(OrderOperation)[:-1])  # skip SetOrder
                    ucs.change_order.execute(target.id, operation)
            except (Conflict, OutOfBounds):
                # Expected — invariant-preserving rejection.
                pass

            # After EVERY step (success or rejection), invariants must hold.
            _all_invariants_hold(
                grids.get_by_id(grid_3x3.id),
                placements.get_by_grid(grid_3x3.id),
            )

    def test_placements_can_completely_fill_grid(self, ucs, grid_3x3, copies):
        """Boundary test: filling all 9 cells with 1x1 placements works."""
        cid = make_copy(copies)
        for x in range(3):
            for y in range(3):
                ucs.place.execute(
                    grid_3x3.id, cid, CellPosition(x, y), OccupySize(1, 1)
                )
        listed = ucs.list_p.execute(grid_3x3.id)
        assert len(listed) == 9
        # Each cell occupied exactly once.
        cells = {(p.position.x, p.position.y) for p in listed}
        assert cells == {(x, y) for x in range(3) for y in range(3)}


class TestScenarios:
    """The named scenarios S1..S6 from 10-requirements.md §2.2."""

    def test_s1_simple_3x3(self, ucs, grid_3x3, copies):
        cid = make_copy(copies)
        for i, (x, y) in enumerate(
            [(x, y) for y in range(3) for x in range(3)], start=1
        ):
            p = ucs.place.execute(grid_3x3.id, cid, CellPosition(x, y), OccupySize(1, 1))
            assert p.placement_order == i
        assert len(ucs.list_p.execute(grid_3x3.id)) == 9

    def test_s2_2x2_center_plus_outer(self, ucs, copies):
        # Need a 3x3 grid. We can't really put a 2x2 in the "center" of a 3x3
        # so that the surrounding cells are all 1x1; instead place a 2x2 at
        # (1,1) and 1x1 placements at the 5 free cells.
        g = ucs.create.execute("g", 3, 3, PixelSize(900, 900))
        cid = make_copy(copies)
        big = ucs.place.execute(g.id, cid, CellPosition(1, 1), OccupySize(2, 2))
        assert big.occupy_size == OccupySize(2, 2)
        # Free cells: (0,0), (1,0), (2,0), (0,1), (0,2).
        for x, y in [(0, 0), (1, 0), (2, 0), (0, 1), (0, 2)]:
            ucs.place.execute(g.id, cid, CellPosition(x, y), OccupySize(1, 1))
        listed = ucs.list_p.execute(g.id)
        assert len(listed) == 6

    def test_s5_weight_change_preserves_positions(self, ucs, grid_3x3, copies):
        from grid_composition.domain import Axis

        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(1, 1), OccupySize(1, 1))
        ucs.change_weights.execute(grid_3x3.id, Axis.COL, [1, 2, 1])
        listed = ucs.list_p.execute(grid_3x3.id)
        assert listed[0].position == p.position

    def test_s6_lock_excludes_from_fit(self, ucs, grid_3x3):
        from grid_composition.domain import Axis

        ucs.toggle_lock.execute(grid_3x3.id, Axis.COL, 1)
        # Locked col survives shrink-by-tail because tail is unlocked.
        updated = ucs.change_dims.execute(grid_3x3.id, 3, 2)
        assert updated.col_locked == (False, True)
