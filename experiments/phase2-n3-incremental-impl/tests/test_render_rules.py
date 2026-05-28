"""RENDERING_EXPORT Rule unit tests (30-design.md §4.1).

Each Rule R-01..R-04 exercised in isolation against the pure helpers /
single-enforcement sites in rendering_export.use_cases.
"""

import uuid

from rendering_export.use_cases import (
    _cumulative_boundaries,
    resolve_effective_crop,
)
from shared.render_contracts import CopyRenderSpec, GridLayout, PlacementView


def _spec(auto=None, manual=None):
    return CopyRenderSpec(
        rotation="None", flip_x=False, flip_y=False,
        scaling_mode="UniformContain", alignment="MiddleCenter",
        auto_crop=auto, manual_crop=manual,
    )


# ---- R-02 ManualCropOverridesAutoCrop (the single application site) --------
def test_r02_manual_present_wins_over_auto():
    spec = _spec(auto=(0xFFFFFFFF, 10), manual=(0.1, 0.1, 0.5, 0.5))
    crop = resolve_effective_crop(spec)
    assert crop.kind == "manual"
    assert crop.value == (0.1, 0.1, 0.5, 0.5)


def test_r02_auto_only():
    spec = _spec(auto=(0xFF000000, 5), manual=None)
    crop = resolve_effective_crop(spec)
    assert crop.kind == "auto"
    assert crop.value == (0xFF000000, 5)


def test_r02_neither_is_none():
    spec = _spec(auto=None, manual=None)
    crop = resolve_effective_crop(spec)
    assert crop.kind == "none"
    assert crop.value is None


def test_r02_does_not_synthesize_manual_and_auto():
    # The forbidden local optimization: combining manual+auto. Must NOT happen.
    spec = _spec(auto=(1, 1), manual=(0.0, 0.0, 1.0, 1.0))
    crop = resolve_effective_crop(spec)
    assert crop.kind == "manual"
    # value is exactly the manual tuple, never a merge of both.
    assert crop.value == (0.0, 0.0, 1.0, 1.0)


# ---- R-04 PixelRectComputedFromWeights -------------------------------------
def test_r04_uniform_boundaries():
    # weights [1,1], canvas 100 -> boundaries [0,50,100].
    assert _cumulative_boundaries((1, 1), 100) == [0, 50, 100]


def test_r04_nonuniform_boundaries():
    # weights [1,3], canvas 100 -> boundaries [0,25,100].
    assert _cumulative_boundaries((1, 3), 100) == [0, 25, 100]


def test_r04_boundaries_start_at_zero_end_at_total():
    b = _cumulative_boundaries((2, 3, 5), 1000)
    assert b[0] == 0 and b[-1] == 1000


def test_r04_floor_rounding_no_gap_no_overlap():
    # weights [1,1,1], canvas 100 (not divisible by 3): floor cumulative.
    b = _cumulative_boundaries((1, 1, 1), 100)
    assert b == [0, 33, 66, 100]
    # adjacent cells abut: widths sum to total, no gap/overlap.
    widths = [b[i + 1] - b[i] for i in range(3)]
    assert sum(widths) == 100


# ---- R-01 / R-03 exercised structurally via DTOs (full UC in test_render_uc)
def test_dto_shapes_are_neutral_str_enums():
    spec = _spec()
    # rotation / scaling_mode / alignment are plain str (no producer enums).
    assert isinstance(spec.rotation, str)
    assert isinstance(spec.scaling_mode, str)
    assert isinstance(spec.alignment, str)


def test_placement_view_carries_uuid_identity():
    cid = uuid.uuid4()
    pv = PlacementView(copy_id=cid, x=0, y=0, occupy_w=1, occupy_h=1, order=1)
    assert isinstance(pv.copy_id, uuid.UUID)
    layout = GridLayout(
        grid_rows=1, grid_cols=1, col_weights=(1,), row_weights=(1,),
        canvas_w=10, canvas_h=10, placements=(pv,),
    )
    assert layout.placements[0].copy_id == cid
