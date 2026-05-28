# IMPLEMENTATION_NOTES_N3 — RENDERING_EXPORT incremental add (n=3)

> This file is *additive*. The copied n=2 `IMPLEMENTATION_NOTES.md` /
> `README.md` are left untouched. This documents only the RENDERING_EXPORT
> increment and the n=3 contract-scaling result (Addendum G).

## 1. What was added on top of the committed n=2 codebase

The entire `experiments/phase2-cocompose-impl/` tree (src/, tests/, compose.py,
conftest, notes) was copied verbatim into `experiments/phase2-n3-incremental-impl/`.
Then, with **no edits to existing Rule / UseCase / failure-reason semantics**:

| File | Change | Kind |
| --- | --- | --- |
| `src/shared/render_contracts.py` | **NEW** neutral DTOs `PlacementView` / `GridLayout` / `CopyRenderSpec` | shared kernel |
| `src/shared/ports.py` | **APPEND** `GridLayoutPort` / `CopyRenderSpecPort` Protocols | shared kernel |
| `src/grid_composition/use_cases.py` | **APPEND** method `get_grid_layout(grid_id) -> GridLayout \| None` | native projection |
| `src/image_variant_management/use_cases.py` | **APPEND** method `get_copy_render_spec(copy_id) -> CopyRenderSpec \| None` | native projection |
| `src/rendering_export/` | **NEW** Capability (`__init__`, `domain`, `events`, `failures`, `use_cases`) | consumer |
| `tests/conftest.py` | **APPEND** `render` fixture (no producer edits) | test wiring |
| `tests/test_render_*.py` | **NEW** rule/UC/event/anchor/property/boundary tests | tests |
| `compose.py` | **UPDATE** to wire all 3 Capabilities | demo |

## 2. RENDERING Decision-ownership self-audit

| Decision | Owner | Honored? |
| --- | --- | --- |
| z-order of items (R-01) | RENDERING UC-01 | yes — `sorted(..., key=order)` once |
| effective crop / R-08 application (R-02) | RENDERING UC-02 | yes — single fn `resolve_effective_crop` |
| pixel geometry (R-04) | RENDERING UC-01 | yes — `_cumulative_boundaries` + `_cell_to_pixel` |
| placement validity (GRID R-01/R-02) | GRID | **read only**, never re-judged |
| copy-setting validity (IMGVAR R-06/R-07) | IMGVAR | **read only**, never re-validated |

Forbidden actions (BOM §5.1) all avoided: RENDERING never mutates placements or
copies, never re-validates crop values, never re-runs GRID placement rules, and
never imports a producer domain type.

## 3. Adapter line-count self-report (target 0)

- **RENDERING ↔ GRID**: 0 adapter lines. `RenderingExportUseCases(grid_layout=grid, ...)`
  passes the real `GridCompositionUseCases` directly; it satisfies `GridLayoutPort`
  natively (runtime `isinstance(grid, GridLayoutPort)` is True).
- **RENDERING ↔ IMGVAR**: 0 adapter lines. The real `ImageVariantManagementUseCases`
  is passed directly as the `CopyRenderSpecPort` (`isinstance(imgvar, CopyRenderSpecPort)`
  is True).
- **GRID ↔ IMGVAR** (carried over from n=2): still 0.

No `class *Adapter`, no wrapper functions, no UUID↔str or Result↔value conversion
exists on any boundary. `compose.py` prints `ADAPTER LINE COUNT AT BOUNDARY: 0`
for all three edges.

## 4. RENDERING domain non-coupling self-report

`src/rendering_export/` imports only from `shared` and from itself:
`from shared.ports import CopyRenderSpecPort, GridLayoutPort`,
`from shared.render_contracts import CopyRenderSpec, GridLayout, PlacementView`,
`from shared.result/eventbus`, plus `rendering_export.*`. It does **not** import
`grid_composition` or `image_variant_management` anywhere.

Verified three ways:
1. `tests/test_render_boundary.py` parses every `rendering_export` source via AST
   and asserts no `import`/`from` node has a forbidden root (passes).
2. Independent grep shows the only textual occurrences of the producer names are
   in comments/docstrings, not import statements.
3. Dynamic closure check: importing `rendering_export.use_cases` with producer
   modules removed from `sys.modules` does not re-pull them (passes).

## 5. Producer additions — kind self-report (native projection, not adapter)

- `GridCompositionUseCases.get_grid_layout` — a **method added to the existing
  UseCases container**, mapping its own `GridCanvas` + placements to the neutral
  `GridLayout`. It uses only existing repositories (`_grids.get_by_id`,
  `_placements.get_by_grid`); **no new getter was needed**. Returns `None` when
  the grid is absent (C-REPO-NOTFOUND). It is not a standalone class and adds no
  Rule.
- `ImageVariantManagementUseCases.get_copy_render_spec` — a **method added to the
  existing UseCases container**, mapping its own `ImageCopy` to the neutral
  `CopyRenderSpec` (enums → `.value` str, crop aggregates → plain tuples).
  Returns `None` when the copy is absent. Not a standalone class, adds no Rule.

Neither projection alters any existing Rule, UseCase, or canonical failure-reason
name. They are the n=3 analogue of how `ImageVariantManagementUseCases.exists()`
natively satisfied `ImageCopyExistencePort` in n=2.

**Caveat recorded for Addendum G**: these projections are a *retrofit* onto the
frozen n=2 producers. The contract permits it (C-CONSUMER-PORTS NOTE), but it is
the real cost of n=3: a consumer addition required touching both producers. This
supports the contract's own suggestion that read-boundary projection methods
should ideally be designed-in up front rather than retrofitted.

## 6. R-08 (ManualCropOverridesAutoCrop) application

R-08 is **declaration-only** in IMGVAR (the n=2 `ImageCopy` deliberately allows
manual_crop and auto_crop to coexist). It is **applied** in RENDERING UC-02 at a
single site, `resolve_effective_crop(spec)`:

```
if spec.manual_crop is not None: -> EffectiveCrop("manual", spec.manual_crop)   # auto ignored
elif spec.auto_crop is not None: -> EffectiveCrop("auto",   spec.auto_crop)
else:                            -> EffectiveCrop("none",   None)
```

The neutral `CopyRenderSpec` exposes both crops unresolved, so RENDERING is the
*only* place R-08 is decided. `AT-02` (manual+auto → "manual") passes, including
the negative assertion that the result equals the manual tuple and is never a
synthesis of the two (the forbidden local optimization).

## 7. RENDERING-local MUST_DECIDE_AND_DOCUMENT (≥ 3)

1. **Pixel rounding policy**: `floor` applied to **cumulative** weight boundaries
   (`(total_px * cumsum) // total_weight`), not per-cell width rounding. This
   guarantees adjacent cells abut with no gap/overlap and every rect stays within
   the canvas. (30-design §2.3.)
2. **RenderDescriptor dict schema**: `{grid_id, canvas_w, canvas_h, items:[{copy_id,
   px,py,pw,ph, effective_crop:{kind,value}, scaling_mode, alignment, rotation,
   flip_x, flip_y}]}` produced by `RenderModel.to_descriptor()`. Deterministic.
3. **EffectiveCrop representation type**: a frozen dataclass `EffectiveCrop(kind:str,
   value:Any)` where `kind ∈ {"manual","auto","none"}` (backed by `CropKind` enum,
   C-ENUM) and `value` carries the raw crop tuple opaquely (never re-validated).
4. **UC-03 event hygiene**: `build_render_model` (UC-01) and
   `export_render_descriptor` (UC-03) share a private `_assemble_render_model`
   that emits no event, so each UC emits exactly one event (UC-03 emits
   `RenderDescriptorExported` only, not also `RenderModelBuilt`).

## 8. unclear / suspected_overreach

- **None blocking.** The contract (C-CONSUMER-PORTS) wired both read boundaries
  with zero adapters; no boundary had to be left unwired.
- **suspected_overreach: none.** RENDERING confines itself to read + projection;
  it owns only rendering decisions.
- **Recorded contract observation (not a violation)**: incremental n=3 required
  retrofitting projection methods onto the two frozen producers (§5 caveat). The
  consumer wiring itself stayed at 0 adapters, but "producer additions = 0" was
  NOT achievable — two native projection methods had to be added. Per the
  contract's own note this is expected and is the n=3 finding to log in
  Addendum G: the contract scales the *consumer* side to 0 adapters, at the cost
  of a small, native producer retrofit.
