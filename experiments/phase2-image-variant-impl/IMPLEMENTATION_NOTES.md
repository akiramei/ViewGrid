# Implementation Notes — IMAGE_VARIANT_MANAGEMENT (Phase 2 v0.1)

Companion to the source under `src/image_variant_management/` and the tests
under `tests/`. Generated per the `40-ai-implementation-prompt.md` Section A
contract.

---

## 1. Language and framework

- Python 3.11+ (tested on Python 3.13.1).
- Dependencies: `pytest` (test runner), `Pillow` (real `ImageDecoder`).
  Both are standard, widely available, and explicitly allowed by the prompt.
- Rationale: prompt variant C.1 sanctions Python to mirror the
  GRID_COMPOSITION trial conditions. Python's `dataclass(frozen=True,
  slots=True)` + `Enum` minimises the gap between the YAML spec and the
  domain code, which is essential when the spec is the authority.

## 2. Self-audit (per the prompt's POST-IMPLEMENTATION SELF-AUDIT — seven checks)

> The prompt header lists "六項目" but actually enumerates **seven** items.
> See §"Spec gap" below. Both YAML/MD are otherwise consistent; this is the only
> contradiction encountered.

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Each Rule (R-01..R-07, R-09..R-11) is enforced in **one** place | PASS | R-01/R-02 in `use_cases.py::import_image_asset`; R-03 in `create_image_copy`; R-04..R-07, R-09..R-11 in the corresponding domain value-object/`__post_init__` constructors plus a single UC mapping layer that surfaces `ValueError` as the canonical failure |
| 2 | R-08 is NOT enforced in this Capability | PASS | `domain.ImageCopy.__post_init__` deliberately permits non-null `auto_crop` AND `manual_crop` simultaneously; UC-12/UC-13 never touch the other field. AT-04 asserts both can coexist. `RenderingExportStub.select_effective_crop` documents where the override **would** be applied (and is unused by the UseCase layer). |
| 3 | Each UC is `input → result` | PASS | Every UC is a single method on `ImageVariantManagementService` returning `Result[T]` (an `Ok` or `Failure`). No globals; all dependencies are injected at construction. |
| 4 | Event emission is independently testable | PASS | `events.EventBus.recorded` lets tests inspect the published sequence without coupling to repository state. `tests/test_events.py` asserts emission separately from state-change tests. |
| 5 | UC-02 has NO cascade-delete | PASS | `use_cases.delete_image_asset` *only* calls `copy_repo.get_by_asset_id` and refuses with `DependentCopiesExist`. No call to `copy_repo.delete` exists anywhere in UC-02. |
| 6 | Anchor tests AT-01..AT-10 all pass | PASS | `pytest tests/test_anchors.py -v` — see §3 below for individual results. |
| 7 | ≥ 5 `MUST_DECIDE_AND_DOCUMENT` items recorded | PASS | §5 below lists 9 such items. |

## 3. Anchor test results (AT-01..AT-10)

| ID | Status | Notes |
|---|---|---|
| AT-01 | PASS | `test_at_01_hash_duplicate_returns_existing` — identical bytes return identical asset, exactly one `ImageAssetImportedAsDuplicate`, zero `ImageAssetImported` |
| AT-02 | PASS | `test_at_02_dependent_copies_block_delete` — `DependentCopiesExist` payload carries both dependent copy IDs |
| AT-03 | PASS | `test_at_03_auto_crop_partial_null_rejected` — `target_color_argb` set + `threshold=None` → `InvalidAutoCropSettings` |
| AT-04 | PASS | `test_at_04_auto_and_manual_crop_coexist` — both fields survive a sequence of changes; turning ManualCrop off leaves AutoCrop intact |
| AT-05 | PASS | `test_at_05_manual_crop_overflow_rejected` — `x=0.6, width=0.5` → `InvalidManualCropFractions` |
| AT-06 | PASS | `test_at_06_rename_to_none_succeeds` |
| AT-07 | PASS | `test_at_07_rename_empty_string_rejected` |
| AT-08 | PASS | `test_at_08_create_copy_unknown_asset` — `NotFound(entity_kind="ImageAsset")` |
| AT-09 | PASS | `test_at_09_exists_false_after_delete` — both UC-16 return value and `ImageCopyDeleted` event |
| AT-10 | PASS | `test_at_10_random_walk_runs` + the actual 1000-step walk in `test_random_walk_1000_steps` |

## 4. Random-walk test results

- `test_random_walk_1000_steps` (seed 42, 1000 steps): **PASS**
- `test_random_walk_alt_seed` (seed 2026, 500 steps): **PASS**

The walker mixes valid and invalid parameters, exercises every state-changing
UseCase, and verifies R-02 / R-03 / R-06 / R-07 + "no orphaned blob" after
**every** step. During development the walker caught a real over-tight
formulation of the "no orphaned blob" check (an earlier draft asserted "every
previously-deleted path stays deleted forever", which conflicts with hash
re-imports legitimately resurrecting the same blob path). The check was
re-formulated as "every blob in storage is referenced by some live asset",
which is the correct R-02/storage-coupling invariant. Specification was not
changed; the **invariant predicate in the test** was sharpened. (Recorded as
a `MUST_DECIDE_AND_DOCUMENT` item — see §5.)

## 5. MUST_DECIDE_AND_DOCUMENT (≥ 5 — actual: 9)

These are decisions the spec deliberately leaves to the implementer (40-prompt
§ ALLOWED + §MUST_DECIDE_AND_DOCUMENT). Each is recorded so a future audit can
reconstruct intent.

1. **Timestamp timezone.** `domain.utc_now()` returns `datetime.now(timezone.utc)`. The spec does not specify a timezone (90-feasibility-notes.md §2.2 #3 explicitly flags this as under-specified). Choice: UTC.

2. **Repository "not found" representation.** Repositories return `None` for missing entities (vs. raising). The UseCase layer maps `None` to `NotFound`. This concentrates failure-construction in one layer and keeps repositories purely query-driven.

3. **Image decoder.** Two implementations are shipped: `PillowImageDecoder` (real, uses `PIL.Image.open` + `verify()`) and `MockImageDecoder` (deterministic in-memory map). Tests use the mock for speed and determinism; `test_boundaries.py::test_pillow_decoder_handles_real_png` exercises the real one against a freshly generated PNG.

4. **Hash implementation.** Standard library `hashlib.sha256(blob).hexdigest()`. Output is lowercase hex (matched by `domain.ImageAsset.__post_init__` validation). No third-party crypto library is required.

5. **`ImageBlobStorage` stub.** In-memory `dict[str, bytes]` keyed by `blobs/{file_hash}`. Identical hashes produce identical paths (R-02 alignment). The real implementation would be a filesystem or object-storage adapter and is owned by `WORKSPACE_MANAGEMENT`.

6. **`AutoCropSettings` / `ManualCropFraction` representation.** Frozen `@dataclass`es with `__post_init__` invariant checks. Aggregate-null semantics ("OFF") is expressed by `ImageCopy.auto_crop: AutoCropSettings | None` and `ImageCopy.manual_crop: ManualCropFraction | None`. The constructors enforce R-06 / R-07 ranges.

7. **Enum representation.** Python `enum.Enum` with `.value` matching the YAML's string forms (`"UniformContain"`, `"None"`, etc.). This keeps wire/serialisation forms identical to the spec.

8. **Shared value objects (`OccupySize` / `PixelSize`).** Placed in `src/image_variant_management/shared/`. The spec explicitly forbids redefining them (10-requirements.md §5, 20-capability-bom.md §4.3, 30-design.md §3.3). For the experiment scope (single-Capability deliverable) the most honest representation is a `shared` subpackage that both Capabilities would import from in a multi-Capability codebase — this physically encodes "co-owned, do not duplicate". The cross-check against `docs/capability-bom-sample/21-grid-composition.yaml` confirmed an exact field-for-field match (`width: int_positive, height: int_positive`). Alternatives considered and rejected: (a) **duplicate** locally — violates the "don't redefine" rule and the boundary-cost observation in 90-feasibility-notes §Addendum C; (b) **stub the import from a hypothetical sibling package** — leaves the experiment uncompilable.

9. **`EventBus` mechanism.** Synchronous in-memory pub/sub with a recorded buffer (`EventBus.recorded`). The prompt's "AI 任意" gives full discretion; this keeps event emission testable independently of side-effects and is the minimum machinery required to satisfy the audit-requirement of independent event verification.

## 6. Decision-ownership self-audit

| Decision class | Owner per 20-capability-bom.md §6 | Where in code |
|---|---|---|
| `domain_decision` | UseCase + Domain | `domain.py` + `use_cases.py` (validation maps) |
| `validation_decision` | UseCase layer | `use_cases.py` only |
| `workflow_decision` | UseCase layer | `use_cases.py::import_image_asset` flow |
| `ui_interaction_decision` | Out of scope | not implemented (correct) |
| `persistence_decision` | Repository / Infra | `repositories.py` interfaces; in-memory stubs only |
| `rendering_decision` | RENDERING_EXPORT | `adjacent_stubs/rendering_export.py` (DOCUMENTED, never invoked by UCs) |
| `history_decision` | HISTORY_MANAGEMENT | `adjacent_stubs/history_management.py` (subscribes only) |
| `cascade_decision` | Upper Coordinator | NOT owned in this Capability — UC-02 refuses with `DependentCopiesExist` only |

**Critical forbidden-but-tempting checks:**

- **R-08 override logic:** *NOT* implemented. Verified: `change_auto_crop_settings` and `change_manual_crop_settings` write to one field each and leave the other untouched; AT-04 asserts both fields survive.
- **UC-02 cascade-delete:** *NOT* implemented. Verified: `delete_image_asset` returns `DependentCopiesExist` before any deletion side effect occurs.

## 7. R-08 in this Capability — "declaration only" expression

R-08 (`ManualCropOverridesAutoCrop`) is acknowledged but not enforced here.
Concretely:

- `domain.ImageCopy` permits both fields to be non-null at the same time.
- `use_cases.change_auto_crop_settings` never touches `manual_crop`.
- `use_cases.change_manual_crop_settings` never touches `auto_crop`.
- `AT-04` (`test_at_04_auto_and_manual_crop_coexist`) asserts coexistence and
  asserts the no-cross-mutation behaviour.
- `adjacent_stubs/rendering_export.py::RenderingExportStub.select_effective_crop`
  documents where the rule **would** be applied — but neither the UseCases nor
  any test of this Capability calls it as part of asserting state. The
  boundary test that does call it (`test_rendering_export_stub_picks_manual_when_both_set`)
  checks the **stub**, not the Capability under test, and verifies the
  IMAGE_VARIANT_MANAGEMENT side retains both values.

## 8. Boundary handling: GRID_COMPOSITION

Limited reads of `docs/capability-bom-sample/20-capability-bom.md` and
`21-grid-composition.yaml` — strictly to check:

1. `OccupySize` / `PixelSize` field shape — confirmed identical (`width: int_positive, height: int_positive`).
2. The shape of the `ImageCopyExists` cross-Capability query — confirmed: GRID's UC-05 takes `[grid_id, copy_id, position, occupy_size]`, has `ImageCopyExists` as a precondition, and a `UnknownCopyId` failure. Our UC-16 returns a plain `bool` (matching the YAML's `output: bool`), which is what the GRID side expects.

No reads beyond those needed for these two checks. Specifically did NOT open
`30-design.md` of GRID or any UC body other than UC-05.

## 9. Cross-reference: unclear items (target < 5; actual: 3)

The hypothesis was that "v0.2 norms applied at v0.1 stage" should produce
fewer `unclear` items than GRID v0.1 (which had 6) and be comparable to GRID
v0.2 (which had 5). Found:

1. **Spec count contradiction.** The prompt header says "POST-IMPLEMENTATION SELF-AUDIT (六項目)" but enumerates **seven** items. Both YAML and the rest of the MD are internally consistent — YAML doesn't restate the count. Treating the enumerated list as authoritative.
2. **UC-05 + R-11.** `UC-05.failure_reasons` in the YAML lists `[NotFound, InvalidAlignment, InvalidScalingMode, InvalidOccupySize, InvalidTransform]` — but R-11 (`copy_name` must be `null` or non-empty) is enforced at the Domain layer (R-11 `enforced_at: domain_model`). If a caller passes `copy_name=""`, the Domain rejects it. The YAML does not name `InvalidCopyName` in UC-05's `failure_reasons`. Resolution: returned `InvalidCopyName` from UC-05 when `copy_name == ""`, on the principle that the Domain invariant binds even where the YAML UC table omits it. Flagging as ambiguous — a strict reading of UC-05's failure list would forbid `InvalidCopyName` here.
3. **`InvalidImageData.detail` shape.** The YAML payload says `detail: string` but does not specify granularity (one of an enumerated set vs. free-form). Chose free-form descriptive strings ("decoder error: ...", "image bytes could not be decoded").

## 10. `suspected_overreach` items (target: 0; actual: 0)

None identified. The Capability boundary stayed clean:

- No code that interprets or applies AutoCrop/ManualCrop pixels.
- No code that derives or stores rendered output.
- No code that decides Placement/cascade behaviour for downstream Capabilities.
- No code that manages workspaces or DB schemas.
- No projection logic for auto-generated copy names (W-5 in 30-design.md is explicit that this is UI's job).

## 11. Subjective assessment

The hypothesis — "v0.2 norms applied at v0.1 stage produce higher quality than
GRID's actual v0.1" — held up well in this implementation. The sample arrived
with `canonical_failure_reasons` already enumerated (with payloads), the
`MUST_DECIDE_AND_DOCUMENT` third category fully fleshed out, all ten anchor
tests pre-specified at the AT-id level, and the 1000-step random walk
mandated. There was very little ambiguity to negotiate during coding; the
biggest "decision" beyond mechanical translation was the Python-specific
question of where to place shared value objects, and even that was
unambiguous after consulting the boundary YAML.

What still felt under-specified:

- **R-11 / UC-05 interaction** (recorded as `unclear` #2). The YAML's
  per-UseCase `failure_reasons` list and the Rule ledger together cover the
  whole surface, but at the granularity of "which UC may emit which failure
  for which exact input" there are seams. This is the same class of gap GRID
  v0.1's audit caught for `Swap-self-exclusion` and `NotFound payload` — the
  norm has reduced the *number* of gaps but not eliminated the *kind*.
- **The 7-vs-6 count contradiction** in 40-prompt is a small but real audit
  finding — a v0.2-graduation review pass would catch this.
- **The "no orphaned blob" invariant** lives in 30-design.md §6.2 as a single
  bullet but the precise predicate ("storage never has a path not referenced
  by a live asset" vs. "deleted paths are never resurrected") is left
  implicit. The random walk caught this as a real ambiguity during
  development.

New gaps not foreseen in 90-feasibility-notes.md:

- **Decoder-error → failure mapping.** Whether decoder exceptions (vs.
  return-None) should produce `InvalidImageData` is not pinned in the spec.
  Conservative choice: catch both, map both to `InvalidImageData`.
- **Event no-op suppression.** When a setter is called with the current
  value (e.g. `change_scaling_mode(copy_id, UNIFORM_CONTAIN)` on a copy
  already in that mode), should an event still fire? The YAML's
  `emitted_by: UC-NN` is silent. Chose to suppress no-op events — fewer
  spurious history entries — and tested the choice
  (`test_setter_no_op_emits_no_event`).

Was I tempted to violate "R-08 declaration only" or "UC-02 cascade refusal"?
Mildly, on the R-08 side: when writing `change_auto_crop_settings`, my
keyboard wanted to add "and if AutoCrop is now off, drop ManualCrop too" as
a tidiness move. The sample's explicit prohibition + the AT-04 test
specification stopped this cleanly — the regression would have shown up
immediately. On UC-02, the explicit `[!IMPORTANT]` block plus the named
failure reason `DependentCopiesExist` made the constraint feel natural; I did
not feel temptation there. This is a successful illustration of why the
methodology emphasises explicit non-goals.

The boundary references to GRID_COMPOSITION (read with strict limits to
§ value_objects and one UseCase signature) did **not** pull me into other
docs. The 40-prompt's "境界調整負荷の最たる例" warning made the cost feel
visible — the entire boundary-handling effort was about 10 minutes of
reading two short YAML sections, vs. an unbounded budget if everything had
been open. The decision to physically locate shared value objects in a
`shared/` subpackage is the single biggest architectural artifact of that
boundary, and the methodology produced it almost mechanically.

Overall: **the inherited-norms hypothesis is supported**, but the residual
gaps (UC-failure-list × Rule-ledger seams, no-op event semantics,
predicate-precision of test-level invariants) suggest that v0.3 norms might
fruitfully add (a) machine-checkable cross-references between
`canonical_failure_reasons.applies_to` and per-UC `failure_reasons`, and
(b) explicit "no-op semantics" guidance per setter UC.
