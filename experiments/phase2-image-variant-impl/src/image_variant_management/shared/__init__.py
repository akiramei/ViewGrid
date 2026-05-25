"""Shared value objects co-owned with GRID_COMPOSITION.

Per 30-design.md §3.3 and 20-capability-bom.md §4.3, ``PixelSize`` and
``OccupySize`` are defined identically in both Capabilities and MUST NOT be
redefined. They live in this ``shared`` subpackage to make the cross-Capability
contract explicit: both IMAGE_VARIANT_MANAGEMENT and GRID_COMPOSITION would
import from here in a real codebase. See IMPLEMENTATION_NOTES.md §"Shared value
objects" for the decision rationale.
"""

from .value_objects import OccupySize, PixelSize

__all__ = ["OccupySize", "PixelSize"]
