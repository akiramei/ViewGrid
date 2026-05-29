"""GRID AT-07 / AT-08: 1000-step random walk preserving R-01, R-02, R-06.

seed-fixed for reproducibility (30-design.md §6.3).
"""
from __future__ import annotations

import random
import uuid

from grid_composition.domain import CellPosition, occupied_cells
from grid_composition.enums import OrderOperation
from grid_composition.use_cases import GridCompositionUseCases
from shared.events import RecordingBus
from shared.result import Ok
from shared.value_objects import OccupySize, PixelSize


class AllExist:
    def exists(self, copy_id):  # noqa: ANN001
        return True


def _check_invariants(uc, gid, rows, cols):
    placements = uc.list_placements(gid)
    # R-01: all within bounds.
    for p in placements:
        assert p.position.x >= 0 and p.position.y >= 0
        assert p.position.x + p.occupy_size.width <= cols
        assert p.position.y + p.occupy_size.height <= rows
    # R-02: no overlaps.
    seen: set[tuple[int, int]] = set()
    for p in placements:
        cells = occupied_cells(p.position, p.occupy_size)
        assert not (cells & seen), "overlap detected"
        seen |= cells
    # R-06: orders unique; AT-08: list is z-order ascending and dense 1..N.
    orders = [p.placement_order for p in placements]
    assert len(orders) == len(set(orders))
    assert orders == sorted(orders)
    if placements:
        assert sorted(orders) == list(range(1, len(placements) + 1))


def test_at_07_at_08_random_walk_1000_steps():
    rng = random.Random(20260529)
    uc = GridCompositionUseCases(image_copy_existence=AllExist(), bus=RecordingBus())
    rows, cols = 4, 4
    res = uc.create_grid_canvas("rw", rows, cols, PixelSize(400, 400))
    assert isinstance(res, Ok)
    gid = res.value.id
    placement_ids: list[uuid.UUID] = []

    for _ in range(1000):
        op = rng.choice(["place", "move", "swap", "resize", "order", "remove"])
        if op == "place":
            pos = CellPosition(rng.randrange(cols), rng.randrange(rows))
            size = OccupySize(rng.randint(1, 2), rng.randint(1, 2))
            r = uc.place_image_copy(gid, uuid.uuid4(), pos, size)
            if isinstance(r, Ok):
                placement_ids.append(r.value.id)
        elif op == "move" and placement_ids:
            pid = rng.choice(placement_ids)
            uc.move_placement(pid, CellPosition(rng.randrange(cols), rng.randrange(rows)))
        elif op == "swap" and len(placement_ids) >= 2:
            a, b = rng.sample(placement_ids, 2)
            uc.swap_placements(a, b)
        elif op == "resize" and placement_ids:
            pid = rng.choice(placement_ids)
            uc.resize_placement_occupancy(pid, OccupySize(rng.randint(1, 2), rng.randint(1, 2)))
        elif op == "order" and placement_ids:
            pid = rng.choice(placement_ids)
            oper = rng.choice(list(OrderOperation))
            n = len(uc.list_placements(gid))
            if oper is OrderOperation.SetOrder:
                uc.change_placement_order(pid, oper, order_value=rng.randint(1, max(1, n)))
            else:
                uc.change_placement_order(pid, oper)
        elif op == "remove" and placement_ids:
            pid = rng.choice(placement_ids)
            r = uc.remove_placement(pid)
            if isinstance(r, Ok):
                placement_ids.remove(pid)
        _check_invariants(uc, gid, rows, cols)

    _check_invariants(uc, gid, rows, cols)
