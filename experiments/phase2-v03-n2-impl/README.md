# phase2-v03-n2-impl (Phase A)

Fresh generation of GRID_COMPOSITION + IMAGE_VARIANT_MANAGEMENT under
**Codebase Convention Contract v0.3**, with the consumer read ports
**built in from the start** (C-CONSUMER-PORTS, up-front mandate) even though
no consumer (RENDERING_EXPORT) exists yet.

## Layout (src/)

```
src/
  shared/
    value_objects.py    # OccupySize, PixelSize (1 definition, C-SHARED-PLACEMENT)
    result.py           # Ok, Err (C-RESULT)
    events.py           # synchronous RecordingBus / NullBus (C-EVENTBUS)
    ports.py            # ImageCopyExistencePort + GridLayoutPort + CopyRenderSpecPort
    render_contracts.py # neutral DTOs PlacementView / GridLayout / CopyRenderSpec
  grid_composition/     # GridCompositionUseCases (UC-01..11), get_grid_layout pre-loaded
  image_variant_management/  # ImageVariantManagementUseCases (UC-01..17),
                             # exists() + get_copy_render_spec() pre-loaded
tests/                  # rules, UC happy/failure, events, AT-01..AT-10 each,
                        # 1000-step random walks, compose integration, read-port DTO
compose.py              # n=2 wiring (zero adapters)
```

## Run

```
python -m pytest experiments/phase2-v03-n2-impl/ -q
python experiments/phase2-v03-n2-impl/compose.py
```
