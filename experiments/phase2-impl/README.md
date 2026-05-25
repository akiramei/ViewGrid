# Phase 2 Feasibility Experiment — GRID_COMPOSITION

Implementation of the `GRID_COMPOSITION` Capability described in
`docs/capability-bom-sample/`. Built from the sample documents alone, with
no reference to the ViewGrid codebase.

## Language and framework

**Python 3.11+ with pytest.** Rationale:

- `30-design.md §2.1` requires Rule predicates to be **pure functions**.
  Python's `def` + `@dataclass(frozen=True)` is the lightest way to
  express this and tests it directly.
- `R-07` (`CellPositionAndOccupySizeAreImmutableInOnePlacement`) maps
  cleanly to `frozen=True` dataclasses with `dataclasses.replace()` as
  the "with_X" pattern.
- `30-design.md §6` requires unit + property-based tests with no UI; a
  CLI/API-style test harness is enough. pytest fixtures keep the
  use-case dependency wiring concise.
- The sample explicitly leaves language and framework choice to the AI,
  and the experiment compares Capability satisfaction rather than UI
  fidelity, so no GUI framework is needed.

## Layout

```
experiments/phase2-impl/
├── README.md                      <- this file
├── IMPLEMENTATION_NOTES.md        <- decisions / self-audit
├── pyproject.toml
├── src/
│   └── grid_composition/
│       ├── __init__.py            <- public surface
│       ├── errors.py              <- canonical failure-reason names
│       ├── domain.py              <- Entities + value objects (R-03,4,5,7)
│       ├── rules.py               <- pure-function Rules (R-01,2,6,9)
│       ├── events.py              <- canonical Events + EventBus
│       ├── repositories.py        <- Protocols + in-memory stubs
│       └── use_cases.py           <- UC-01..UC-11
├── adjacent_stubs/                <- IMAGE_VARIANT_MANAGEMENT / HISTORY /
│   ├── image_variant_management.py   RENDERING_EXPORT boundary stubs only
│   ├── history_management.py
│   └── rendering_export.py
└── tests/
    ├── conftest.py
    ├── test_rules.py              <- Rule unit tests (R-01..R-09)
    ├── test_use_cases.py          <- UC happy + failure paths
    ├── test_events.py             <- Event emission + non-emission on failure
    ├── test_invariants.py         <- Invariant-after-operation + S1/S2/S5/S6
    └── test_boundary.py           <- Capability-boundary self-checks
```

## Build / run

No build step. Python 3.11+ and pytest are sufficient.

```bash
cd experiments/phase2-impl
python -m pytest -v
```

Expected: **97 tests pass**, in ~0.1s.

## Implementation notes

See `IMPLEMENTATION_NOTES.md` for:

- Decision Ownership self-audit (per `40-ai-implementation-prompt.md`
  POST_IMPLEMENTATION_SELF_AUDIT).
- `unclear` / `suspected_overreach` items.
- `MUST_DECIDE_AND_DOCUMENT` items (per `90-feasibility-notes.md §2.3`).
- Notes on contradictions between Markdown and YAML inputs (none found).
