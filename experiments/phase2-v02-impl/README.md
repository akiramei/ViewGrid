# GRID_COMPOSITION — Phase 2 v0.2 implementation

A reference implementation of the `GRID_COMPOSITION` Capability produced
from the v0.2 sample documents under `docs/capability-bom-sample/` alone.

## Language / framework

- **Python 3.10+** (developed on 3.13)
- **pytest** + **hypothesis** for unit and property-based tests

**Why Python?** The sample places its semantic emphasis on Rule
preservation and Decision ownership, not on a particular runtime.
Python lets the implementation stay close to the documents
(`@dataclass(frozen=True)` mirrors R-07 immutability; tagged-union-style
failures map cleanly to canonical_failure_reasons). It also makes the
1000-step property test easy to write with `hypothesis`.

## Build

No build step is required. The package is plain Python.

```bash
pip install -e ".[test]"
```

Or, simply install the test dependencies:

```bash
pip install pytest hypothesis
```

## Run tests

From this directory:

```bash
python -m pytest
```

For verbose output:

```bash
python -m pytest -v
```

To run just the anchor tests:

```bash
python -m pytest tests/test_anchors.py -v
```

## Files

- `grid_composition/` — implementation (Domain Model + UseCases +
  Repositories + Events + Rules)
- `tests/` — pytest suite (rules, use cases, events, anchors + random walk)
- `IMPLEMENTATION_NOTES.md` — self-audit, unclear list, MUST_DECIDE_AND_DOCUMENT items

See `IMPLEMENTATION_NOTES.md` for the POST_IMPLEMENTATION_SELF_AUDIT
results and the experiment-relevant findings.
