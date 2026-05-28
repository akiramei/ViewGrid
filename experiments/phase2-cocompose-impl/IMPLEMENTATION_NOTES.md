# IMPLEMENTATION_NOTES — phase2-cocompose-impl

Co-generation of `GRID_COMPOSITION` + `IMAGE_VARIANT_MANAGEMENT` into one
codebase under `docs/capability-bom-sample/00-convention-contract.md`
(candidate E, step 2). Written fresh from the sample documents only; no existing
ViewGrid code or prior `experiments/` generations were read.

Result headline: **adapter line count at the GRID↔IMAGE_VARIANT boundary = 0.**

---

## 1. Adapter line count (self-report)

**0 lines.**

`ImageVariantManagementUseCases` exposes `exists(copy_id: uuid.UUID) -> bool`
(running UC-16 `ImageCopyExists` internally) and is therefore a structural match
for `shared.ports.ImageCopyExistencePort`. It is injected *directly* into
`GridCompositionUseCases(image_copy_existence=imgvar)`:

- `compose.py`: no adapter class. `imgvar` is passed as the Port.
- `tests/test_compose.py`: no adapter. `imgvar` is the Port.
- production code path (`src/`): GRID stores the Port reference and calls
  `self._copies.exists(copy_id)` — one call, no conversion.

The contract eliminated the two-stage adapter that step 1 (Addendum E) required:
- step 1 needed `UUID -> str` because the two sides disagreed on identity type;
  here both sides use `uuid.UUID` (C-IDENTITY), so no conversion.
- step 1 needed `Result[bool] -> bool` because IMAGE_VARIANT returned a wrapped
  result; here UC-16 / `exists()` return a **plain bool** (C-BOUNDARY-IFACE),
  so no unwrapping.

`tests/conftest.py` contains an `AlwaysExistsPort` test double, but that is a
**test stand-in** for exercising GRID in isolation, NOT a boundary adapter; the
compose tests use the real `ImageVariantManagementUseCases` with zero glue.

---

## 2. Contract-compliance self-report (C-IDENTITY .. C-EVENTBUS)

| Contract item | Satisfied | How |
| --- | --- | --- |
| **C-IDENTITY** | yes | All identities are `uuid.UUID`; generated with `uuid.uuid4()` (never `str(uuid.uuid4())`). No `str` identity anywhere. |
| **C-SHARED-PLACEMENT** | yes | `OccupySize` / `PixelSize` defined once in `src/shared/value_objects.py`; both Capabilities `from shared.value_objects import ...`. No local redefinition. `is`-comparison test passes (`test_occupy_size_is_same_type_in_both_capabilities`). |
| **C-VALUE-SEMANTICS** | yes | Both are `@dataclass(frozen=True)`; `_check_positive_int` rejects `bool` (so `OccupySize(True, 1)` raises `TypeError`); values must be `>= 1`. |
| **C-RESULT** | yes | `Ok` / `Err` defined once in `src/shared/result.py`; both Capabilities import them. No `Failure`/`Success` synonyms. |
| **C-LAYOUT** | yes | `src/` layout: `shared/`, `grid_composition/`, `image_variant_management/`. |
| **C-UC-CONTAINER** | yes | `GridCompositionUseCases` and `ImageVariantManagementUseCases`. No `Service` naming. |
| **C-BOUNDARY-IFACE** | yes | `ImageCopyExistencePort` Protocol in `src/shared/ports.py` (`exists(copy_id: uuid.UUID) -> bool`, no Result wrap). GRID UC-05 depends on it (`False -> UnknownCopyId`); IMAGE_VARIANT satisfies it (`exists` runs UC-16). |
| **C-TIMESTAMP** | yes | `datetime.now(timezone.utc)` (UTC, tz-aware) for all timestamps. |
| **C-REPO-NOTFOUND** | yes | Repositories return `None` for "not found" (never raise). |
| **C-ENUM** | yes | `enum.Enum` for `Axis`, `OrderOperation` (GRID) and `Rotation`, `ScalingMode`, `Alignment`, `SourceType` (IMAGE_VARIANT). |
| **C-EVENTBUS** | yes | Single synchronous in-process `RecordingBus` (`src/shared/eventbus.py`) shared by both Capabilities; events recorded for independent observation. |

No contract item was left unsatisfied.

---

## 3. Decision-ownership self-audit

### GRID_COMPOSITION

- `domain_decision` (placement validity) lives in `grid_composition/domain.py`
  pure functions (`fits_within_grid` = R-01, `find_overlaps` = R-02) and entity
  constructors (R-03/R-04/R-05). Called from the UseCase layer only.
- `validation_decision` (input ranges) lives at UseCase entry
  (`change_row_column_weights`, `toggle_row_column_lock`, etc.).
- `workflow_decision`: UC-07 Swap's 4-step procedure incl. the **post-swap A-B
  intersection check** (W-3 / §2.2 UC-07) lives in `swap_placements`. This is the
  documented exception to "one Rule enforcement site" — the extra intersection
  test is UC-07 workflow, not a second copy of R-02 (so NOT `suspected_overreach`).
- `persistence_decision`: repositories are dumb stores; they enforce no Rules
  (R-06 uniqueness, R-01/R-02 are NOT delegated to any unique/check constraint).
- `rendering_decision` / `history_decision`: not present; GRID only emits events.
- ImageCopy is referenced by `copy_id` only and never interpreted (existence
  check via the Port is the sole use).

### IMAGE_VARIANT_MANAGEMENT

- `domain_decision` (setting validity, aggregate both-ness): R-06/R-07 enforced at
  value-object construction (`AutoCropSettings`, `ManualCropFraction`); R-04/R-05/
  R-09/R-10/R-11 at `ImageCopy` construction.
- `workflow_decision`: UC-01 hash-dedup flow in `import_image_asset`.
- `cascade_decision` is **NOT owned**: UC-02 `delete_image_asset` refuses with
  `DependentCopiesExist` when dependents exist; it never auto-cascades.
- `rendering_decision` (R-08 ManualCropOverridesAutoCrop) is **NOT applied**: both
  values are stored and allowed to coexist (AT-04 confirms). No override code.
- No reference to `Placement` (would be a reverse-dependency violation).

---

## 4. POST-IMPLEMENTATION SELF-AUDIT (prompt §POST-IMPLEMENTATION SELF-AUDIT)

1. **One enforcement site per Rule** — yes. GRID R-01/R-02 are single pure
   functions; the UC-07 post-swap check is the documented workflow exception.
   IMAGE_VARIANT rules are each enforced once at construction.
2. **R-08 not applied; UC-02 has no cascade** — yes. No override logic exists;
   UC-02 only refuses (`DependentCopiesExist`).
3. **OccupySize is the same type in both Capabilities** — yes, verified by an
   `is`-comparison test (`grid_domain.OccupySize is img_domain.OccupySize`).
4. **Adapter line count** — 0 (see §1).
5. **GRID Anchor tests AT-01..AT-10** — all pass (`test_grid_anchor.py`).
6. **compose integration tests (2)** — both pass (`test_compose.py`):
   `test_compose_place_existing_copy_succeeds`,
   `test_compose_place_unknown_copy_returns_unknown_copy_id`.
7. **IMAGE_VARIANT Anchor tests AT-01..AT-10** — all pass
   (`test_imgvar_anchor.py`).
8. **Contract C-IDENTITY..C-EVENTBUS** — all satisfied (§2).

---

## 5. MUST_DECIDE_AND_DOCUMENT (Capability-local only; contract items excluded)

### GRID_COMPOSITION (>=3)

1. **Fit (R-08 weight redistribution) shrink algorithm** — when shrinking a
   dimension, unlocked weights are dropped tail-first; locked weights are never
   dropped; if too few unlocked elements exist, the operation maps to
   `WouldOrphanPlacements` (per 30-design.md §R-08, no new failure reason).
   Deterministic.
2. **UC-09 order model** — placement_order is kept dense 1..N at all times;
   reorder operations compute a target index then re-densify (R-06 + OrdersAreDense).
3. **Same-position move optimisation (W-2)** — a no-op move still returns `Ok`
   and still publishes `PlacementMoved` (we did not adopt the optional "skip
   event" optimisation; the contract leaves this free, tests only require "no
   failure").
4. **Repository implementation** — in-memory dict stubs; persistence form is free.

### IMAGE_VARIANT_MANAGEMENT (>=3)

1. **Image decoder** — pluggable `ImageDecoder` callable; default is a toy
   `fake_header_decoder` (`b"IMG:<w>x<h>:..."`) so tests need no real image
   library and can inject mocks. Real deployments inject a PIL-backed decoder.
2. **Hash implementation** — SHA-256 hex lower via `hashlib.sha256` (R-02 key).
3. **ImageBlobStorage stub** — in-memory dict keyed by `blobs/<hash>`; UC-02
   deletes the blob on asset delete (supports the "no orphaned blob" walk check).
4. **Aggregate value-object types** — `AutoCropSettings` / `ManualCropFraction`
   are non-null frozen dataclasses; `ImageCopy.auto_crop` / `.manual_crop` are
   `<aggregate> | None` (None = OFF), so R-06/R-07 both-ness is structural.
5. **Construction-failure mapping** — `ImageCopy` construction failures in UC-05
   are surfaced as canonical `Invalid*` reasons (no new failure reasons created).

---

## 6. unclear / suspected_overreach

- **unclear**: none that blocked wiring. The contract was sufficient to wire the
  boundary with zero adapter (the central question of step 2 — answered yes).
- **suspected_overreach**: none. The only multi-site logic (UC-07 post-swap
  intersection check) is explicitly designated workflow_decision by
  30-design.md §2.2 / §R-02, not a duplicated Rule.
- Minor note (not a contract gap): `InvalidOccupySize` for IMAGE_VARIANT UC-05/
  UC-14 is reachable only if a non-`OccupySize` is passed, because `OccupySize`
  self-validates (C-VALUE-SEMANTICS). The failure reason name is preserved per
  the YAML even though the shared value object makes most invalid sizes
  unconstructable upstream — recorded here for transparency, not as overreach.

---

## 7. Anchor-test pass status

- GRID AT-01..AT-10: pass (incl. AT-03 W-3 asymmetric-swap Conflict and AT-07
  1000-step random walk, seed 20260529).
- IMAGE_VARIANT AT-01..AT-10: pass (incl. AT-10 1000-step random walk, seed
  20260529, asserting R-02/R-03/R-06/R-07).
- compose AT (the two integration tests): pass, no adapter.
