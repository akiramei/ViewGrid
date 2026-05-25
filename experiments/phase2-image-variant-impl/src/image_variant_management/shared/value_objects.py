"""Value objects shared with GRID_COMPOSITION.

Both ``PixelSize`` and ``OccupySize`` are defined identically in
``21-grid-composition.yaml`` value_objects section:

    OccupySize: { width: int_positive, height: int_positive }
    PixelSize:  { width: int_positive, height: int_positive }

The IMAGE_VARIANT_MANAGEMENT sample explicitly says "shared definition — do not
redefine" (10-requirements.md §5, 20-capability-bom.md §4.3, 30-design.md §3.3).

Decision (see IMPLEMENTATION_NOTES.md): keep the type physically in a ``shared``
subpackage of this Capability for the experiment. In a multi-Capability
deployment this module would be promoted to a shared library both Capabilities
depend on, but the experiment scope only ships IMAGE_VARIANT_MANAGEMENT.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class PixelSize:
    """Pixel dimensions of an image. Both axes must be positive (>= 1)."""

    width: int
    height: int

    def __post_init__(self) -> None:
        if not isinstance(self.width, int) or not isinstance(self.height, int):
            raise TypeError("PixelSize width/height must be int")
        if self.width < 1 or self.height < 1:
            raise ValueError(
                f"PixelSize requires positive width/height; got ({self.width}, {self.height})"
            )


@dataclass(frozen=True, slots=True)
class OccupySize:
    """Number of grid cells (width x height) an ImageCopy occupies by default.

    Per R-10 (DefaultOccupySizeMustBePositive), both axes must be >= 1.
    """

    width: int
    height: int

    def __post_init__(self) -> None:
        if not isinstance(self.width, int) or not isinstance(self.height, int):
            raise TypeError("OccupySize width/height must be int")
        if self.width < 1 or self.height < 1:
            raise ValueError(
                f"OccupySize requires positive width/height; got ({self.width}, {self.height})"
            )
