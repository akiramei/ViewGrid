# GRID_COMPOSITION v0.2 — Implementation Notes

This document is the self-audit artifact required by
`40-ai-implementation-prompt.md` §A (OUTPUT FORMAT item 3 and
POST_IMPLEMENTATION_SELF_AUDIT section).

It is written from the perspective of an independent re-experiment that
saw only the seven files under `docs/capability-bom-sample/` (v0.2).
It has not consulted the v0.1 attempt under `experiments/phase2-impl/`,
the ViewGrid source tree, or the repo-root README.

---

## 1. POST_IMPLEMENTATION_SELF_AUDIT (six checks)

### Check 1 — Each Rule has a single guarantee site

| Rule | Owned by | Site in this codebase |
| --- | --- | --- |
| R-01 PlacementMustFitWithinGrid | UseCase layer | `grid_composition/rules.py::fits_within_grid` (single function) — called by UC-02, UC-05, UC-06, UC-07, UC-08. **single site** |
| R-02 PlacementsMustNotOverlap | UseCase layer | `grid_composition/rules.py::find_conflicts` — called by UC-02 (via `_detect_internal_conflicts`), UC-05, UC-06, UC-07, UC-08. **single site for the standard "candidate vs. existing" check** |
| R-02 (UC-07 extension) | UC-07 workflow_decision | `use_cases.py::swap_placements` step (iv) — `if cells_a & cells_b: return Conflict`. Per 30-design §1 R-02 NOTE and the 40-prompt self-audit exception, this is NOT a duplicate of R-02 but the UC-07 workflow_decision. **NOT a suspected_overreach.** |
| R-03 GridDimensionsMustBePositive | Domain Model | `entities.GridCanvas.__post_init__` + `PixelSize.__post_init__` |
| R-04 WeightsMustBePositiveIntegers | Domain Model | `entities.GridCanvas.__post_init__` |
| R-05 WeightArrayLengthMatchesDimension | Domain Model + UC | `entities.GridCanvas.__post_init__` (length check) + `use_cases._fit_weights/_fit_locks` (UC-02 adjustment per 30-design §1 R-05). The split is exactly as the Rule Ledger declares — **not** overreach. |
| R-06 PlacementOrderMustBeUnique | UseCase layer | Constructively maintained by UC-05 (`max + 1`), UC-09 (rebuild dense 1..N), UC-10 (compact). Assertion in UC-09 verifies the constructive invariant; no overreach. |
| R-07 CellPositionAndOccupySizeAreImmutableInOnePlacement | Entity immutability | `entities.Placement` is a `@dataclass(frozen=True)`. |
| R-08 LockedWeightsAreSkippedInFitAdjustment | UseCase layer | `use_cases._fit_weights` / `_fit_locks` — single helper called only from UC-02. |
| R-09 RemovedPlacementOrderMustBeCompacted | UseCase layer | `use_cases.remove_placement` final loop reassigning placement_order. |

**Result:** every Rule has a single owning site as declared.
**suspected_overreach:** none.

### Check 2 — Each UseCase is expressible as input → result single function

Every UC method on `GridCompositionUseCases` is a single method that
takes named arguments and returns a `Result` value (`Ok` | `Err`).
Side effects are scoped to repository writes and event publication on
the success path only — no UC writes on a failure path. Tests
`test_events.py::test_failure_paths_emit_no_events` and
`test_uc01_failure_emits_nothing` cover this directly.

### Check 3 — Event emission independently testable

`grid_composition/events.RecordingBus` is a passive list. Every
`test_events.py` test asserts on the bus contents without re-querying
state, demonstrating the independence required by 30-design §4.

### Check 4 — UI layer Role check

This implementation has no UI layer (the prompt declares UI as out of
scope for the sample). The only callers of UseCases are the tests, which
match the **invokes** Role. No code outside `use_cases.py` and the
private `rules.py` exercises Rule predicates → no UI/Repository
`enforces` Role leakage. `enforces` is owned by the UseCase layer
(via `rules.py`), `owns` is shared by UseCase layer and Domain Model.

### Check 5 — Anchor tests (AT-01 .. AT-10) all pass

```
tests/test_anchors.py::test_at_01_empty_grid_first_placement_order_is_one PASSED
tests/test_anchors.py::test_at_02_move_to_same_position_does_not_self_conflict PASSED
tests/test_anchors.py::test_at_03_swap_asymmetric_sizes_conflict PASSED
tests/test_anchors.py::test_at_04_set_order_pushes_others_down PASSED
tests/test_anchors.py::test_at_05_remove_compacts_orders PASSED
tests/test_anchors.py::test_at_06_not_found_carries_entity_kind PASSED
tests/test_anchors.py::test_at_07_random_walk_preserves_invariants PASSED
tests/test_anchors.py::test_at_08_listed_placements_ascending_z_order PASSED
tests/test_anchors.py::test_at_09_dimension_shrink_orphans PASSED
tests/test_anchors.py::test_at_10_invalid_lock_index PASSED
```

The 1000-step random walk in AT-07 checks R-01, R-02, R-06 (and R-09 by
extension via the dense-order check) after every step. Seed is fixed
via `derandomize=True`.

### Check 6 — MUST_DECIDE_AND_DOCUMENT items (≥ 5 required)

Listed below in section 4. **7 items.**

---

## 2. `unclear` items (issues the documents did not resolve unambiguously)

Numbered for traceability with the parent experiment's report.

### U-1: Cross-grid swap behaviour

The BOM says UC-07 takes two placement IDs, but does not say what happens
if they belong to **different GridCanvas** entities. There is no canonical
failure reason for it (`NotFound` doesn't apply; `OutOfBounds` is wrong;
`Conflict` is the closest).

**Resolution chosen:** return `Conflict(conflicting_placement_ids=(a,b))`.
This treats the operation as semantically undefined and keeps the failure
reason set canonical. **Marked as MUST_DECIDE_AND_DOCUMENT below.**

### U-2: `GridCanvas` disappearance under a `Placement`

The repository interfaces leave open the question of whether a
`Placement` may exist for a non-existent `GridCanvas`. The current code
defensively returns `NotFound(entity_kind="GridCanvas")` if it happens.
This is unreachable in our in-memory wiring, but the docs neither
prohibit nor mandate the defensive behaviour.

### U-3: Repeated `change_row_column_weights` with identical input

Should this emit `RowColumnWeightsChanged` (with before == after) or
suppress? The docs do not say. **Chosen:** always emit — failure is the
only suppression signal documented (30-design §4 IMPORTANT).

### U-4: UC-04 toggling a locked entry that will be removed by next UC-02

The Fit-adjust helper may have to drop a locked weight on shrink. The
docs mention `WouldDestroyLockedAxis` only as background ("ロック要素を
削除する必要が出た場合は WouldDestroyLockedAxis として失敗") but the
v0.2 YAML's `canonical_failure_reasons` does **not** list this name as
a canonical reason. The FORBIDDEN list says I may not add new failure
reasons. Therefore I cannot raise it.

**Resolution chosen:** silent best-effort shrink (drop unlocked tail
first, then locked tail). This is observed to be safe because the
prior R-01 check still rejects shrink that would orphan a placement.
**Marked as MUST_DECIDE_AND_DOCUMENT below.**

This is a candidate sample defect: 30-design §1 R-08 mentions
`WouldDestroyLockedAxis` but `canonical_failure_reasons` omits it, so
the two documents are **internally inconsistent**.

### U-5: `PlacementOrderChanged` emission when order does not actually change

For instance, `BringToFront` on the already-frontmost placement: the
order map is identical before and after. Should the event still fire?
Docs are silent. **Chosen:** emit unconditionally (mirrors U-3).

---

## 3. `suspected_overreach` items

**None.**

In particular:

- **R-02 in UC-07.** The post-swap A/B intersection check in
  `swap_placements` step (iv) is **not** R-02 logic duplicated. Per
  30-design.md §1 R-02 NOTE and §2.2 UC-07, this is the
  workflow_decision of UC-07. The v0.1 experiment (per addendum A.5)
  had this listed as O-1; v0.2's explicit reclassification in the docs
  closes that gap. My implementation does not call `find_conflicts`
  for this check — it uses a direct set-intersection on the two new
  cell sets, distinct from `rules.py::find_conflicts`. So even by
  the strictest code-grep audit, R-02's helper has exactly one
  site.

- **R-06 in UC-09.** I do not maintain a separate
  "orders_are_unique" predicate. The constructive rebuild in UC-09
  (`reordered.insert(new_index, target)` then enumerate 1..N) means
  R-06 holds by construction. The `assert` at the end is a
  programmer-error check, not a domain-decision site. v0.1's O-2 is
  therefore absent here.

---

## 4. MUST_DECIDE_AND_DOCUMENT items (≥ 5 required by v0.2 prompt)

These are decisions the sample documents leave open but the
implementation must take. Each is followed by the choice and rationale.

### MD-1: Timestamp time zone

- **Decision:** `datetime.now(timezone.utc)` everywhere.
- **Rationale:** UTC avoids ambiguity on persistence and matches the v0.1
  feasibility-notes §2.2 item 3 expectation. The docs do not specify.

### MD-2: Repository "not found" representation

- **Decision:** `Optional[T]` (return `None`). No exceptions.
- **Rationale:** UseCase converts `None` into `NotFound(entity_kind, id)`
  per BOM §2.1. Keeps the Capability-internal contract value-based.

### MD-3: Domain failure as values, not exceptions

- **Decision:** UseCases return a `Result` (`Ok | Err`). Exceptions are
  reserved for programmer errors (e.g. structurally invalid
  `OccupySize`).
- **Rationale:** Matches "pure-function verification" requirements of
  R-01 and R-02 (30-design §1) and makes failure paths testable.

### MD-4: Identity type

- **Decision:** `uuid.UUID`, exported as the alias `Id`. New ids come
  from `new_id() -> uuid.uuid4()`.
- **Rationale:** Sample says "opaque identity" (21-yaml type=identity).
  UUID4 satisfies opacity and is collision-safe for the test scale.

### MD-5: Event delivery mechanism

- **Decision:** in-process synchronous bus (`RecordingBus` for tests,
  `NullBus` as default). Events are appended only when the UseCase
  succeeds.
- **Rationale:** 30-design §4.2 explicitly allows AI choice, and
  recommends sync delivery for testability.

### MD-6: Locked weight removal during UC-02 shrink

- **Decision:** Silent best-effort: drop unlocked tail entries first,
  then locked tail entries if still needed. Do **not** invent a new
  failure reason (`WouldDestroyLockedAxis` is not in
  `canonical_failure_reasons`).
- **Rationale:** Honours FORBIDDEN ("failure reason additions
  prohibited"). See `U-4` above — this surfaces a sample inconsistency
  between 30-design §1 R-08 and the YAML.

### MD-7: Cross-grid swap

- **Decision:** Return `Conflict(conflicting_placement_ids=(a, b))`.
- **Rationale:** No canonical failure reason for "different grids";
  Conflict captures the spirit ("these two cannot occupy the same
  cells, definitionally") without inventing a new reason.

---

## 5. Decision Ownership self-attribution (BOM §6)

| Component | Roles held | Notes |
| --- | --- | --- |
| `entities.GridCanvas` / `Placement` | `owns` (domain state) | Pure data, R-03, R-04, R-05 (length), R-07 (immutability). |
| `rules.py` (`fits_within_grid`, `find_conflicts`, `occupied_cells`) | `enforces` (R-01, R-02 standard check) | Pure functions. No I/O. |
| `use_cases.GridCompositionUseCases` | `owns`, `enforces`, `coordinates` | Owns workflow_decision (UC-02 fit, UC-07 swap, UC-09 reorder, UC-10 compact). Calls `rules.py`. |
| `repositories.InMemory*` | `persists` | No Rule enforcement. |
| `repositories.InMemoryImageCopyExistenceCheck` | (stub for IMAGE_VARIANT_MANAGEMENT) | `existence_check_only` per BOM §4.2. |
| `events.RecordingBus` | (subscriber) | No `owns`/`enforces`. |
| Tests | `invokes`, `observes` | Match BOM §7.1 allowed roles. |

Suspicious / Forbidden combinations from BOM §7.2: **none present.**

---

## 6. Spec contradictions / sample defects observed

### Defect 1: README references "Addendum B" which does not exist

`docs/capability-bom-sample/README.md` line 4 says:

> v0.1 → v0.2 の変更点は `90-feasibility-notes.md` Addendum A §A.4 + Addendum B を参照

`90-feasibility-notes.md` contains only Addendum A (lines 285–419). There
is no Addendum B in the file. Trivial doc inconsistency but worth
flagging — the README promises a section the spec docs do not deliver.

### Defect 2: `WouldDestroyLockedAxis` is named but not canonical

30-design.md §1 R-08 says (paraphrased): "if a locked element must be
removed, the operation fails with `WouldDestroyLockedAxis`." But
`21-grid-composition.yaml`'s `canonical_failure_reasons` does not list
`WouldDestroyLockedAxis`, and the FORBIDDEN section forbids inventing
new failure reasons. Since the YAML is canonical per
`40-ai-implementation-prompt.md` §B.2, I treat
`WouldDestroyLockedAxis` as not-a-failure-reason and silently drop the
locked weight on shrink. This is a real underdetermination in v0.2.

### Defect 3: UC-09 SetOrder "rejects" semantics for sibling-list of length 0

If a grid has zero placements, UC-09 cannot be called on any placement
(NotFound first), so this is unreachable. But it's worth noting that
`order_value` upper bound is "N" where N = count of placements; this is
correct as written but only because of the precondition.

### Defect 4: UC-09 input_notes vs. failure_reasons mismatch

`21-yaml` UC-09 `inputs` lists `order_value`, and `input_notes` says
"order_value は operation == SetOrder のときのみ必須." Good. But the
`canonical_failure_reasons.InvalidOrderValue.notes` adds: "他 operation
で order_value が指定された" — i.e. specifying a value when the
operation is **not** SetOrder is also an error. That is a slight
asymmetry with `input_notes` which says "none / null を渡してよい" for
other operations. The two are not strictly contradictory ("must be None"
plus "if you pass a value, that is an error") but the contract could be
crisper. I implemented the stricter reading (extra value → error).

---

## 7. Test inventory

```
tests/conftest.py             fixtures
tests/test_rules.py           14 tests — R-01..R-07 unit coverage
tests/test_use_cases.py       38 tests — UC-01..UC-11 happy + failure paths
tests/test_events.py          13 tests — emission + non-emission on failure
tests/test_anchors.py         10 tests — AT-01..AT-10 incl. 1000-step walk
                              ────────
                              75 tests total
```

All 75 pass. See README.md for how to reproduce.

---

## 8. Closing remark (re. the experiment goal)

The v0.2 sample documents were materially better than I can infer the
v0.1 documents to have been, judging from `90-feasibility-notes.md`
Addendum A. The crucial improvements:

- **AT-03 (swap asymmetric)** is callable directly from §7 Worked
  Example W-3 plus the explicit step (iv) in §2.2 UC-07. I implemented
  the post-swap intersection check naturally on first writing, without
  any random-walk discovery. The v0.1 trigger bug is fully foreclosed.
- **canonical_failure_reasons** (YAML §) removed the v0.1 ambiguity
  about `NotFound` representation. The implementation was direct.
- **MUST_DECIDE_AND_DOCUMENT** as a third category gave me a clear
  place to record cross-grid swap, locked-axis shrink, etc., without
  having to invent a category.

Where v0.2 still leaves gaps:

- **`WouldDestroyLockedAxis` inconsistency** (Defect 2 above) is a v0.2
  regression vs. itself — the Markdown and YAML disagree.
- **Cross-grid swap** is genuinely unspecified.
- **Event-on-no-op** (e.g. BringToFront of frontmost; identical-weights
  re-set) is genuinely unspecified.

If the v0.1 experiment yielded 6 `unclear` and 2 `suspected_overreach`,
this run produced **5 unclear (U-1..U-5)** and **0 suspected_overreach**.
The marginal improvement is real but the documents are not yet
"underdetermination-free."
