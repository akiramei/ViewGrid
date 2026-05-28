# phase2-cocompose-impl

Co-generation of two Capabilities — `GRID_COMPOSITION` and
`IMAGE_VARIANT_MANAGEMENT` — into **one codebase** under the shared
`docs/capability-bom-sample/00-convention-contract.md` (Capability BOM Audit,
candidate E, step 2).

The experiment's question: **can the convention contract eliminate the
hand-written adapter that step 1 (Addendum E) needed at the GRID↔IMAGE_VARIANT
boundary?** Answer here: **yes — adapter line count = 0.**
`ImageVariantManagementUseCases` natively satisfies
`shared.ports.ImageCopyExistencePort` and is injected directly into
`GridCompositionUseCases`.

## Language

Python 3.11+ (developed/tested on 3.13). Chosen because the contract's type
vocabulary — `uuid.UUID`, `@dataclass(frozen=True)`, `typing.Protocol`,
`enum.Enum`, `datetime` with `timezone.utc` — maps directly to the stdlib, so
the boundary Port can be expressed and structurally satisfied with no third-party
dependencies. Tests use `pytest`. No image library is required (a pluggable,
mockable decoder is used).

## Layout (`src/` layout, C-LAYOUT)

```
src/
├── shared/
│   ├── value_objects.py   # OccupySize, PixelSize        (C-SHARED-PLACEMENT)
│   ├── result.py          # Ok, Err                       (C-RESULT)
│   ├── ports.py           # ImageCopyExistencePort        (C-BOUNDARY-IFACE)
│   └── eventbus.py        # RecordingBus (sync in-process, C-EVENTBUS)
├── grid_composition/      # GridCompositionUseCases (UC-01..UC-11, R-01..R-09)
└── image_variant_management/  # ImageVariantManagementUseCases (UC-01..UC-17, R-01..R-11)
compose.py                 # wires both; NO adapter
tests/                     # rule/usecase/event/anchor/random-walk + compose tests
```

## Run the tests

From the repo root (`conftest.py` puts `src/` on `sys.path` automatically):

```bash
python -m pytest experiments/phase2-cocompose-impl/ -q
```

Alternative — set `PYTHONPATH` to the `src` dir explicitly:

```bash
# Windows PowerShell
$env:PYTHONPATH = "experiments/phase2-cocompose-impl/src"
python -m pytest experiments/phase2-cocompose-impl/ -q

# bash
PYTHONPATH=experiments/phase2-cocompose-impl/src python -m pytest experiments/phase2-cocompose-impl/ -q
```

## Run the compose demo

```bash
python experiments/phase2-cocompose-impl/compose.py
```

Prints that `imgvar` is an `ImageCopyExistencePort`, that placing an existing
copy succeeds, that an unknown copy returns `UnknownCopyId`, and
`ADAPTER LINE COUNT AT BOUNDARY: 0`.

## Key result

See `IMPLEMENTATION_NOTES.md` for the full self-audit: adapter line count,
per-contract-item compliance (C-IDENTITY..C-EVENTBUS), decision-ownership audit,
Capability-local MUST_DECIDE decisions, and `unclear`/`suspected_overreach`
(none blocking).
