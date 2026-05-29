"""RENDERING_EXPORT Rule unit tests: R-01, R-02, R-03, R-04 (isolated)."""
from __future__ import annotations

import uuid

from rendering_export.use_cases import _boundaries, cell_to_pixel, resolve_effective_crop
from shared.render_contracts import CopyRenderSpec, GridLayout, PlacementView


def _spec(manual=None, auto=None):
    return CopyRenderSpec(
        rotation="None",
        flip_x=False,
        flip_y=False,
        scaling_mode="UniformContain",
        alignment="MiddleCenter",
        auto_crop=auto,
        manual_crop=manual,
    )


# R-02 ManualCropOverridesAutoCrop ---------------------------------------
def test_r02_manual_overrides_auto():
    crop = resolve_effective_crop(_spec(manual=(0.1, 0.1, 0.5, 0.5), auto=(0xFFFFFFFF, 10)))
    assert crop.kind == "manual"
    assert crop.value == (0.1, 0.1, 0.5, 0.5)


def test_r02_auto_when_no_manual():
    crop = resolve_effective_crop(_spec(manual=None, auto=(0xFF000000, 5)))
    assert crop.kind == "auto"
    assert crop.value == (0xFF000000, 5)


def test_r02_none_when_neither():
    crop = resolve_effective_crop(_spec(manual=None, auto=None))
    assert crop.kind == "none"
    assert crop.value is None


# R-04 PixelRectComputedFromWeights --------------------------------------
def test_r04_uniform_boundaries():
    assert _boundaries((1, 1), 100) == [0, 50, 100]


def test_r04_non_uniform_boundaries():
    assert _boundaries((1, 3), 100) == [0, 25, 100]


def test_r04_cell_to_pixel_uniform():
    layout = GridLayout(2, 2, (1, 1), (1, 1), 100, 100, ())
    p = PlacementView(uuid.uuid4(), 0, 0, 1, 1, 1)
    assert cell_to_pixel(p, layout) == (0, 0, 50, 50)
    p2 = PlacementView(uuid.uuid4(), 1, 1, 1, 1, 2)
    assert cell_to_pixel(p2, layout) == (50, 50, 50, 50)


def test_r04_cell_to_pixel_non_uniform():
    layout = GridLayout(1, 2, (1, 3), (1,), 100, 50, ())
    p = PlacementView(uuid.uuid4(), 1, 0, 1, 1, 1)
    px, py, pw, ph = cell_to_pixel(p, layout)
    assert px == 25 and pw == 75
