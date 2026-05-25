# IMAGE_VARIANT_MANAGEMENT — Phase 2 v0.1 sample implementation

Python 3.11+ implementation of the `IMAGE_VARIANT_MANAGEMENT` Capability from
`docs/capability-bom-sample/image-variant-management/`.

This is an experiment artifact: it implements all UseCases (UC-01..UC-17), all
Rules (R-01..R-11, with R-08 declared but **not** enforced — per the Capability
boundary), and emits all required events. Anchor tests AT-01..AT-10 and the
mandatory 1000-step random walk are included.

## Language and rationale

Python 3.11+. The 40-prompt § C.1 variant explicitly allows specifying Python
to match the GRID_COMPOSITION trial conditions, and Python's lightweight
dataclasses, ``Enum``, and rich Pillow ecosystem produce the cleanest
expression of the value-object-heavy domain.

## Layout

```
experiments/phase2-image-variant-impl/
├── README.md
├── IMPLEMENTATION_NOTES.md
├── pytest.ini
├── src/image_variant_management/
│   ├── __init__.py
│   ├── shared/                  ← OccupySize, PixelSize (co-owned with GRID_COMPOSITION)
│   ├── domain.py                ← Entities + value objects + enums
│   ├── failures.py              ← canonical_failure_reasons + Result wrapper
│   ├── events.py                ← All event types + in-memory EventBus
│   ├── repositories.py          ← Repository interfaces + in-memory stubs
│   ├── image_decoder.py         ← ImageDecoder interface + Pillow + Mock
│   ├── use_cases.py             ← UC-01..UC-17 service class
│   └── adjacent_stubs/          ← Minimal stubs for GRID, HISTORY, RENDERING, WORKSPACE
└── tests/
    ├── conftest.py              ← Shared fixtures (deterministic clock + id)
    ├── test_rules.py            ← R-01..R-07, R-09..R-11 unit tests
    ├── test_use_cases.py        ← UC happy/failure paths
    ├── test_events.py           ← Event emission tested separately from state
    ├── test_anchors.py          ← AT-01..AT-10
    ├── test_random_walk.py      ← 1000-step property-based walk
    └── test_boundaries.py       ← Cross-Capability boundary contracts
```

## Build / run

Prerequisites: Python 3.11+ and `pip install pytest pillow`.

```powershell
cd experiments\phase2-image-variant-impl
python -m pytest
```

Or directly:

```powershell
python -m pytest tests -v
```

## What this Capability deliberately does NOT do

* It does **not** implement R-08 (ManualCrop overrides AutoCrop). Both values
  coexist on `ImageCopy`. The override interpretation is owned by
  `RENDERING_EXPORT`. The `RenderingExportStub` only documents the rule.
* It does **not** auto-cascade-delete dependent `ImageCopy`s when an
  `ImageAsset` is deleted. UC-02 returns `DependentCopiesExist` with the
  dependent IDs and lets the upper Coordinator decide.
* It does **not** rely on DB constraints for R-02 hash uniqueness — the
  UseCase layer queries `find_by_hash` itself.
* It does **not** compute auto-generated copy names (that is a UI projection).

See `IMPLEMENTATION_NOTES.md` for the full self-audit.
