# phase2-v03-n3-impl (Phase B)

Verbatim copy of `phase2-v03-n2-impl` (Phase A) with **RENDERING_EXPORT added
only** -- no producer/shared edits. Validates that pre-loading the consumer
read ports (contract v0.3 C-CONSUMER-PORTS) makes incremental consumer
addition **producer-free** (retrofit = 0).

## What changed vs Phase A

- NEW: `src/rendering_export/` (`RenderingExportUseCases`, domain, events,
  failures).
- NEW: `tests/test_render_rules.py`, `test_render_uc.py`,
  `test_render_anchors.py`, `test_render_random_walk.py`,
  `test_render_boundary.py`.
- EDITED: `compose.py` (wires 3 Capabilities; 1 wiring line for the consumer).
- UNCHANGED (byte-identical): `src/shared`, `src/grid_composition`,
  `src/image_variant_management`.

## Consumer wiring (zero adapters)

```python
render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)
```

`grid` natively satisfies `GridLayoutPort`; `imgvar` natively satisfies
`CopyRenderSpecPort` -- both from Phase A. R-08 (ManualCropOverridesAutoCrop)
is applied here (IMGVAR declares it only). `RenderDescriptor` str()-ifies
identities so `json.dumps` succeeds (C-IDENTITY-BOUNDARY).

## Run

```
python -m pytest experiments/phase2-v03-n3-impl/ -q
python experiments/phase2-v03-n3-impl/compose.py
```
