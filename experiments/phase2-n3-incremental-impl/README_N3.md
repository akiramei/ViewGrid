# README_N3 — phase2-n3-incremental-impl (GRID × IMGVAR × RENDERING_EXPORT)

n=3 scaling check for the Codebase Convention Contract v0.2. The committed n=2
implementation (`phase2-cocompose-impl`) was copied here verbatim, then
`RENDERING_EXPORT` was added incrementally as a read-only consumer of both
producers, wired with zero hand-written adapters. See `IMPLEMENTATION_NOTES_N3.md`
for the self-audit; the copied `README.md` / `IMPLEMENTATION_NOTES.md` describe
the n=2 base unchanged.

## Layout

```
src/
├── shared/
│   ├── value_objects.py     # OccupySize, PixelSize         (C-SHARED-PLACEMENT)
│   ├── result.py            # Ok, Err                       (C-RESULT)
│   ├── eventbus.py          # RecordingBus                  (C-EVENTBUS)
│   ├── render_contracts.py  # NEW neutral DTOs              (C-CONSUMER-PORTS)
│   └── ports.py             # ImageCopyExistencePort + NEW GridLayoutPort / CopyRenderSpecPort
├── grid_composition/        # + native projection get_grid_layout
├── image_variant_management/# + native projection get_copy_render_spec
└── rendering_export/        # NEW consumer Capability (UC-01..03, R-01..04)
tests/                       # 101 carried-over n=2 tests + 39 new RENDERING tests
compose.py                   # wires all 3 Capabilities, prints adapter line count 0
```

## Running

`tests/conftest.py` inserts `src/` onto `sys.path`, so no `PYTHONPATH` is needed.

Run the full suite (from the repo root):

```
python -m pytest experiments/phase2-n3-incremental-impl/ -q
```

Run the 3-Capability compose demo:

```
python experiments/phase2-n3-incremental-impl/compose.py
```

The demo creates a grid, an ImageCopy (with both manual and auto crop set),
places the copy, and has `RenderingExportUseCases.build_render_model` read both
producers through the ports. It prints the render item count, the pixel rect, the
R-08-resolved crop kind (`manual`), and `ADAPTER LINE COUNT AT BOUNDARY: 0` for
each of the three boundaries.

### Manual PYTHONPATH alternative (without conftest)

```
# bash
PYTHONPATH=experiments/phase2-n3-incremental-impl/src python -m pytest experiments/phase2-n3-incremental-impl/tests -q
# PowerShell
$env:PYTHONPATH = "experiments/phase2-n3-incremental-impl/src"; python -m pytest experiments/phase2-n3-incremental-impl/tests -q
```

## Test groups

| File | Covers |
| --- | --- |
| `test_shared_value_objects.py`, `test_grid_*.py`, `test_imgvar_*.py`, `test_compose.py` | carried-over n=2 (101 tests, unchanged) |
| `test_render_rules.py` | R-01..R-04 unit (resolve_effective_crop, cumulative boundaries) |
| `test_render_use_cases.py` | UC-01/02/03 happy + NotFound failures + events |
| `test_render_anchor.py` | AT-01..AT-08 + render integration (z-order × crop × geometry) |
| `test_render_property.py` | 1000-step seed-fixed random walk invariants |
| `test_render_boundary.py` | static + dynamic check: no producer-domain imports in rendering_export |
