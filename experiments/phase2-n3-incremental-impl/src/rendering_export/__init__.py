"""RENDERING_EXPORT Capability (focused v0.1) — consumer of GRID + IMGVAR.

Reads GRID geometry/placements and IMGVAR copy render settings via the shared
consumer read ports (C-CONSUMER-PORTS) and the neutral DTOs in
src/shared/render_contracts.py. It is the authority for the *rendering* view:
z-order (R-01), effective crop / R-08 application (R-02), dangling exclusion
(R-03), and pixel geometry from weights (R-04).

This package MUST NOT import grid_composition or image_variant_management domain
types (Forbidden by C-CONSUMER-PORTS / BOM §5.1). It depends only on shared.
"""
