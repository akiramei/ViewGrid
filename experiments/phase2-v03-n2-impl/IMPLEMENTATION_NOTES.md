# Implementation Notes — Phase A (phase2-v03-n2-impl)

## Goal

Generate n=2 (GRID + IMGVAR) from scratch under contract v0.3 with the
consumer read ports **pre-loaded** (C-CONSUMER-PORTS up-front), so a later
consumer (RENDERING) can be added with zero producer retrofit.

## What was built in from the start (consumer absent)

- `src/shared/render_contracts.py` — neutral DTOs `PlacementView` / `GridLayout`
  / `CopyRenderSpec`. No producer enums leak; rotation / scaling_mode / alignment
  are neutral `str`.
- `src/shared/ports.py` — `ImageCopyExistencePort` (existing n=2 bool boundary)
  PLUS `GridLayoutPort` + `CopyRenderSpecPort` (read ports, pre-loaded).
- `GridCompositionUseCases.get_grid_layout(grid_id) -> GridLayout | None`
  (native projection; None for missing grid).
- `ImageVariantManagementUseCases.get_copy_render_spec(copy_id) -> CopyRenderSpec | None`
  (native projection; enums `.value`-stringified; None for missing copy).
  **R-08 is NOT applied here** (declaration-only; application belongs to RENDERING).

## Existing n=2 boundary (unchanged thinking)

- GRID UC-05 (`place_image_copy`) depends on `ImageCopyExistencePort.exists`.
- IMGVAR satisfies the Port natively via `exists(copy_id) -> bool` (runs UC-16 logic).
- Zero standalone adapters — both sides share the one Protocol in `shared/ports.py`.

## Contract conventions applied

uuid.UUID identity (uuid.uuid4 factory, no str() internally), Ok/Err result,
None for not-found, UTC tz-aware timestamps, enum.Enum, `{Capability}UseCases`
naming, `src/` layout, single shared OccupySize/PixelSize/Ok/Err definitions.

## Tests

GRID + IMGVAR mandatory categories (Rule unit, UC happy, UC failure, Event
emission), AT-01..AT-10 for each Capability, 1000-step seed-fixed random walks
for both, compose integration (existing copy places OK / unknown -> UnknownCopyId),
and read-port tests confirming the projections return the neutral DTOs.

All green: 67 passed.

## Speculative cost note

The n=2 here carries `render_contracts.py`, two extra Protocols, and two
projection methods purely on faith that a consumer will arrive. They are pure
read mappings (domain -> neutral DTO) with no behavior, so the cost is small
(~80 LOC of projection + DTO) and fully exercised by the read-port tests. The
bet: this small up-front cost removes ALL producer retrofit when RENDERING
lands in Phase B.
