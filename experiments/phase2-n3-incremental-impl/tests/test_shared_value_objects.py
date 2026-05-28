"""Shared value object tests (C-SHARED-PLACEMENT, C-VALUE-SEMANTICS).

Includes the critical is-comparison test: OccupySize must be the SAME type in
both Capabilities (POST-IMPLEMENTATION SELF-AUDIT item 3).
"""

import pytest

from shared.value_objects import OccupySize, PixelSize


def test_occupy_size_is_same_type_in_both_capabilities():
    # Import the symbol via each Capability's module to prove identity.
    import grid_composition.domain as grid_domain
    import image_variant_management.domain as img_domain

    assert grid_domain.OccupySize is img_domain.OccupySize
    assert grid_domain.OccupySize is OccupySize
    assert grid_domain.PixelSize is img_domain.PixelSize is PixelSize


def test_occupy_size_rejects_bool_as_int():
    # C-VALUE-SEMANTICS: bool must NOT be accepted as int.
    with pytest.raises(TypeError):
        OccupySize(True, 1)
    with pytest.raises(TypeError):
        OccupySize(1, False)


def test_occupy_size_rejects_below_one():
    with pytest.raises(ValueError):
        OccupySize(0, 1)
    with pytest.raises(ValueError):
        OccupySize(1, 0)


def test_occupy_size_frozen():
    o = OccupySize(2, 3)
    with pytest.raises(Exception):
        o.width = 5  # frozen dataclass


def test_pixel_size_rejects_bool_and_below_one():
    with pytest.raises(TypeError):
        PixelSize(True, 10)
    with pytest.raises(ValueError):
        PixelSize(0, 10)


def test_value_equality():
    assert OccupySize(2, 3) == OccupySize(2, 3)
    assert PixelSize(100, 200) == PixelSize(100, 200)
