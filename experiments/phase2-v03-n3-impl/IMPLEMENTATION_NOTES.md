# Implementation Notes — Phase B (phase2-v03-n3-impl)

## Procedure

1. Copied the ENTIRE contents of `experiments/phase2-v03-n2-impl/` verbatim.
2. Added ONLY `src/rendering_export/` + `tests/test_render_*.py`, and edited
   `compose.py` to wire 3 Capabilities.
3. Did NOT touch `src/grid_composition`, `src/image_variant_management`, or
   `src/shared`. `get_grid_layout` / `get_copy_render_spec` already existed
   from Phase A and are consumed as-is.

## Producer + shared diff = 0 (self-reported, how verified)

`diff -r` on each of `src/shared`, `src/grid_composition`,
`src/image_variant_management` between the n2 and n3 dirs reports no
differences (exit 0). A per-file `sha256sum` comparison of all 20
producer/shared `.py` files reports OK for every file. **Producer retrofit = 0.**

## New files vs Phase A

- `src/rendering_export/` : `__init__.py`, `domain.py`, `events.py`,
  `failures.py`, `use_cases.py` (`RenderingExportUseCases`).
- `tests/test_render_rules.py`, `test_render_uc.py`, `test_render_anchors.py`,
  `test_render_random_walk.py`, `test_render_boundary.py`.
- `compose.py` edited (the only modified pre-existing file).

## RENDERING domain non-coupling

RENDERING imports only `shared.ports` (GridLayoutPort, CopyRenderSpecPort),
`shared.render_contracts` (GridLayout, PlacementView, CopyRenderSpec),
`shared.result`, `shared.events`. It does NOT import grid_composition or
image_variant_management. `tests/test_render_boundary.py` parses every
rendering_export module's AST and asserts neither producer root is imported.

## R-08 application (R-02 here)

`ManualCropOverridesAutoCrop` is declaration-only in IMGVAR; RENDERING is the
application site. `resolve_effective_crop` (used by UC-02 and UC-01):
manual present -> manual (auto ignored); else auto; else none. AT-02 passes
(manual+auto -> "manual").

## RenderDescriptor str-ification (C-IDENTITY-BOUNDARY, G.7)

Internal `RenderModel` / `RenderItem` keep `copy_id` / `grid_id` as
`uuid.UUID`. `RenderDescriptor.from_model` + `RenderItem.to_dict` `str()` the
identities at the output boundary, so `json.dumps(descriptor.to_dict())`
succeeds (tested in
`test_render_anchors.py::test_render_descriptor_json_dumps_succeeds`).

## Consumer wiring adapter line count = 0

Wiring is a single constructor call in `compose.py`:

```python
render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar, bus=bus)
```

`grid` and `imgvar` are passed directly (they structurally satisfy the read
Protocols). No standalone adapter class, no conversion function -- a grep for
`Adapter` in `src/rendering_export/` returns nothing.

## Speculative-cost impression

The up-front bet paid off completely: because n=2 already exposed the read
projections + neutral DTOs, adding RENDERING required ZERO producer edits
(byte-identical) and ZERO adapters. The speculative cost carried at n=2 was
small and self-contained (one DTO module + two Protocols + two pure projection
methods, all behavior-free read mappings), and it was already test-covered
before the consumer existed. Contrast with the v0.2 "retrofit later" path
(Addendum G), which required editing the producers to add projections when the
consumer arrived. Up-front read ports turned incremental consumer addition into
a fully producer-free operation.
