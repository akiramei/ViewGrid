# IMPLEMENTATION_NOTES — Phase 2 GRID_COMPOSITION

## 1. Decision Ownership self-audit

Per `40-ai-implementation-prompt.md` POST_IMPLEMENTATION_SELF_AUDIT.

### 1.1 Rule guarantee locations

| Rule | Owner module             | Owner symbol                                         | Sole location? |
| ---- | ------------------------ | ---------------------------------------------------- | -------------- |
| R-01 | `rules.py`               | `placement_fits_within_grid`                         | Yes            |
| R-02 | `rules.py`               | `placement_overlaps`                                 | **Mostly** — see suspected_overreach below |
| R-03 | `domain.py`              | `GridCanvas.__post_init__` + `PixelSize.__post_init__` | Yes            |
| R-04 | `domain.py`              | `GridCanvas.__post_init__`                           | Yes            |
| R-05 | `domain.py` + `use_cases.py` | `GridCanvas.__post_init__` (length match) + `_fit_array` (UC-02 adjustment) | Split as the BOM mandates: `enforced_at: [domain_model, use_case_layer]` |
| R-06 | `rules.py` + `use_cases.py` | `orders_are_unique` (predicate) + UC-09 renumbering (constructive) | Yes (predicate in one place; UC-09 satisfies by construction and verifies via predicate) |
| R-07 | `domain.py`              | `@dataclass(frozen=True)` on `Placement` + `with_*` returning new instances | Yes            |
| R-08 | `use_cases.py`           | `_fit_array` (UC-02 only)                            | Yes            |
| R-09 | `rules.py`               | `compact_orders` (called by UC-10)                   | Yes            |

### 1.2 Use Case → single-function correspondence

Each `<UCName>.execute(...)` is one function call with no hidden state
between invocations. Repository and EventBus are injected once at
construction; calling `execute()` twice with the same args against an
empty repo yields the same result. Verified by inspection.

### 1.3 Event emission independent of state change

`events.RecordingBus` captures every published event into a list. Tests
in `test_events.py` assert per-UC that exactly one event of the canonical
type is published on success, and zero events on failure. The bus is a
pure subscriber registry — it does not decide whether to emit.

### 1.4 UI-layer `owns` / `enforces`

**No UI layer was implemented**, per `40-ai-implementation-prompt.md`
SCOPE ("UI レイアウト... 対象外"). Therefore there is no UI possession
of any Role. If a UI were added, it would only be allowed to use
`invokes` / `observes` / `projects`.

## 2. `unclear` items

Things the sample documents left ambiguous; I made a choice and document
it here.

| # | Topic | What was unclear | What I did |
|---|-------|------------------|-----------|
| U-1 | "Grid not found" failure name | `21-grid-composition.yaml` lists `GridExists` as a `precondition` for UC-02/03/04/05 but does NOT give a canonical failure-reason name for the "grid missing" case. | Treat it as a programming error: UC-02 raises `InvalidDimensions` with a clear `detail`, others propagate a `KeyError`-equivalent. **This may be wrong**; the BOM is silent. |
| U-2 | "Placement not found" failure | Same: `PlacementExists` is a precondition but has no canonical failure reason in UC-06/07/08/09/10. | Raise plain `KeyError` (NOT a `UseCaseError` subclass). Tests do not exercise the "not found" path. |
| U-3 | UC-09 `SetOrder` value semantics | Inputs are `placement_id, operation`. For `SetOrder` it's unclear if the value comes via a separate field or is encoded in `operation`. | Added a keyword arg `order_value: int \| None`. `SetOrder` is the only operation that uses it. |
| U-4 | `Conflict` payload shape | `30-design.md §1 R-02` says "result must include conflicting placement_id". Plural? | Modeled as a tuple `conflicting_placement_ids` to allow >1. |
| U-5 | timestamp timezone | Sample says "timestamp" with no tz convention. | UTC, tz-aware via `datetime.now(timezone.utc)`. Documented in `domain._now`. (Cf. `90-feasibility-notes.md §2.2-3`.) |
| U-6 | Shrink-axis "must drop locked" semantics | `30-design.md §1 R-08` mentions "`WouldDestroyLockedAxis`" as a sketch but the BOM does NOT list this failure reason. | Collapsed into `InvalidDimensions` (FORBIDDEN forbids adding new failure reasons). |

## 3. `suspected_overreach` items

| # | Rule | Where | Why this might be a violation |
|---|------|-------|------------------------------|
| O-1 | R-02 | `use_cases.SwapPlacements.execute` does an inline check of A's new footprint vs B's new footprint (intersection of cell sets). `rules.placement_overlaps` is also used for the third-party check. | R-02 logic is now in **two locations**: `rules.placement_overlaps` and `use_cases.SwapPlacements`. The inline check exists because R-02's "exclude IDs" semantics excludes both A and B from each other's third-party check; the A-vs-B-at-new-positions case is not expressible via `placement_overlaps` alone without re-introducing one of them. A cleaner refactor would extend `placement_overlaps` to accept "synthetic placements" representing the post-swap state. I kept the inline check to keep `rules.placement_overlaps` purely about candidate-vs-existing. |
| O-2 | R-06 | `use_cases.ChangePlacementOrder.execute` calls `rules.orders_are_unique` as a *post-condition assertion* (line "post-condition R-06 violated"). The renumbering itself constructs 1..N which is unique by construction. | The construction is the sole **guarantor**; the assertion is a defensive check. I left it in to surface implementation bugs, not as a R-06 enforcement. Could be removed if `suspected_overreach` is strict. |

## 4. `MUST_DECIDE_AND_DOCUMENT` items (per `90-feasibility-notes.md §2.3`)

At least 5 documented decisions; here are 9:

1. **Programming language: Python 3.11+.** Justification in `README.md`.
2. **Timestamps: UTC, tz-aware (`datetime`).** See U-5 above.
3. **`placement_order` representation: dense 1..N integers.** This was
   stated in `30-design.md §1 R-09` for UC-10, and I applied it
   consistently to UC-09 so that `BringToFront`/`SendToBack`/etc. also
   produce dense numbering. The doc does **not** explicitly require
   dense numbering across UC-09.
4. **Repository "not found" returns `None`.** `30-design.md §5.1` shows
   `GetById(...) -> GridCanvas | None`. I followed this signature
   literally (Python `Optional`), not raising.
5. **Atomic UCs (UC-02, UC-07) use a `transaction()` context manager
   on the in-memory repository.** Real persistence backends would
   substitute their own mechanism; the use case calls it generically
   via `_maybe_transaction`.
6. **Event bus is synchronous in-process.** `30-design.md §4.2` permits
   this and recommends it for test ergonomics.
7. **Failure-reason types are frozen dataclasses inheriting from
   `Exception`.** Allows pattern matching by type and structured
   payload (Conflict carries IDs).
8. **`OccupySize.width / height` interpretation: width = column-axis,
   height = row-axis.** Stated by `30-design.md §3.3` but worth
   reconfirming.
9. **`Axis` and `OrderOperation` as `Enum`** rather than strings, to
   keep the failure-reason set fixed at compile time.

## 5. Contradictions between Markdown and YAML

Per `40-ai-implementation-prompt.md` "Markdown と YAML 矛盾 → YAML 正"，
I scanned all four input docs and **found no material contradiction**.
Notes:

- `20-capability-bom.md §3` and `21-grid-composition.yaml §rules` list
  identical Rule names, kinds, and enforcement locations.
- `21-grid-composition.yaml §entities.owned.GridCanvas.fields` includes
  `col_locked / row_locked` with `default: all_false`; `20-capability-bom.md`
  does not redundantly list defaults but does not contradict.
- `30-design.md §1 R-08` mentions a *possible* failure reason
  `WouldDestroyLockedAxis` which is **not** in the YAML's `failure_reasons`
  for UC-02. I treated YAML as canonical: collapsed into `InvalidDimensions`.

## 6. POST_IMPLEMENTATION_SELF_AUDIT (four checks)

1. **Each Rule has a single guarantee location?** Yes for R-01, R-03,
   R-04, R-07, R-08, R-09. R-05 is intentionally split (per BOM
   `enforced_at: [domain_model, use_case_layer]`). R-02 has the
   suspected_overreach O-1 above. R-06 has suspected_overreach O-2.
2. **Each UseCase is a single-function correspondence?** Yes —
   `<UseCase>.execute(...)` is the entry; no inter-call hidden state
   beyond what the injected Repository holds.
3. **Event emission is independently testable?** Yes — `RecordingBus`
   captures events, asserted per UC in `test_events.py`.
4. **UI layer owns/enforces?** N/A — no UI layer in this PoC. The
   forbidden roles are not held by any component.

## 7. Test coverage summary

```
tests/test_rules.py        28 tests (Rule unit tests, R-01..R-09)
tests/test_use_cases.py    44 tests (UC happy + failure paths)
tests/test_events.py       19 tests (event emission + non-emission)
tests/test_invariants.py    6 tests (incl. 1000-step random walk)
tests/test_boundary.py      5 tests (Capability boundary self-checks)
                          -----
                            97 total, all passing
```

## 8. What I struggled with

- **R-02 in UC-07 was subtly buggy.** My first version excluded both
  swap targets from each other's overlap check, which incorrectly
  passes a case where A's new footprint and B's new footprint mutually
  cover a cell. The 1000-step random walk caught it. The fix is the
  inline post-swap intersection check noted in `suspected_overreach`
  O-1. This is **direct evidence** that the BOM's R-02 algorithm
  description is sufficient *given correct implementation*, but the
  "除外対象" wording (`30-design.md §1 R-02`) glosses over the A-vs-B
  case.
- **"Grid not found / Placement not found" failure names.** The BOM
  treats these as preconditions but never names the failure reason.
  Adding a new failure-reason name was forbidden. I had to choose
  between (a) raising `InvalidDimensions` (which the BOM does list),
  (b) raising a plain `KeyError`, or (c) silently returning `None`. I
  used (a) for UC-02 (grid-id-bearing) and (b) for placement-id-bearing
  UCs. This was arbitrary; the docs should have a canonical answer.
- **UC-09 `SetOrder` value plumbing.** The YAML `inputs:` field lists
  `[placement_id, operation]` but `SetOrder` semantically needs a
  third argument. I added a keyword arg. This is a real documentation
  gap.

## 9. What would have helped

1. A canonical failure-reason name for "Entity not found by id". The
   BOM should add `NotFound` (with the entity kind in the payload) or
   declare it out of scope explicitly.
2. The `SetOrder` operation's value channel made explicit (either in
   the `inputs` list as a third arg or in a sub-table).
3. A worked example for the swap edge case (A at (0,0) 1x1, B at
   (1,0) 2x1) showing the expected outcome. The 除外対象 wording is
   not unambiguous on its own.
4. A small reference test suite (5-10 assertions) shipped with the
   sample to anchor the most common interpretation ambiguities.
