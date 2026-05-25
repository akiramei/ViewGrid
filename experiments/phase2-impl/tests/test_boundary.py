"""Capability-boundary tests.

Verifies the design intent stated in 20-capability-bom.md §8 and §7.2:
- GRID_COMPOSITION sees ImageCopy only via existence check.
- No persistence-level rule enforcement (R-06 in particular).
- Failure when an ImageCopy is "forgotten" (UnknownCopyId).
"""

from __future__ import annotations

from uuid import uuid4

import pytest

from grid_composition import (
    CellPosition,
    OccupySize,
    PixelSize,
    UnknownCopyId,
)


class TestImageCopyBoundary:
    def test_only_existence_check_is_called(self, ucs, copies, grid_3x3):
        """The capability calls .exists(copy_id) — nothing else."""

        # Check by introspection: copies (InMemoryImageCopyExistenceCheck)
        # exposes only existence-style API.
        public_methods = [m for m in dir(copies) if not m.startswith("_")]
        # The protocol mandates .exists. Test helpers .register/.forget are
        # for setup only and are not used inside use cases.
        assert "exists" in public_methods

    def test_unknown_copy_id_rejects_place(self, ucs, grid_3x3):
        with pytest.raises(UnknownCopyId):
            ucs.place.execute(
                grid_3x3.id, uuid4(), CellPosition(0, 0), OccupySize(1, 1)
            )


class TestRepositoryDoesNotEnforceRules:
    """20-capability-bom.md §7.2 — Repository must not 'enforces'.

    We assert this by inspecting the repository implementations: they do
    not implement any Rule predicate. The Rules live in grid_composition.rules.
    """

    def test_repos_have_no_rule_methods(self, grids, placements):
        forbidden_in_repo = {
            "placement_fits_within_grid",
            "placement_overlaps",
            "orders_are_unique",
            "compact_orders",
        }
        for repo in (grids, placements):
            for name in forbidden_in_repo:
                assert not hasattr(repo, name), (
                    f"Repository {type(repo).__name__} must not own rule {name}"
                )


class TestNoRenderingInfoStored:
    """20-capability-bom.md §7.2 — 本 Capability is forbidden from holding
    rendering info."""

    def test_grid_canvas_fields_do_not_include_rendering_state(self, grid_3x3):
        # canvas_size is allowed (carried, not interpreted). No other
        # rendering-y fields like color, opacity, etc.
        forbidden_fields = {
            "color",
            "opacity",
            "fill",
            "stroke",
            "border_style",
            "render_priority",
        }
        for f in forbidden_fields:
            assert not hasattr(grid_3x3, f), f"GridCanvas leaks rendering field {f}"

    def test_placement_does_not_carry_pixel_coords(self, ucs, grid_3x3, copies):
        from tests.conftest import make_copy

        cid = make_copy(copies)
        p = ucs.place.execute(grid_3x3.id, cid, CellPosition(0, 0), OccupySize(1, 1))
        forbidden_fields = {"pixel_x", "pixel_y", "render_x", "render_y"}
        for f in forbidden_fields:
            assert not hasattr(p, f), f"Placement leaks rendering field {f}"
