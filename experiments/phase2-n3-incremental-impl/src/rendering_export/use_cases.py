"""RenderingExportUseCases — UC-01..UC-03 (C-UC-CONTAINER naming).

Consumer of GRID + IMGVAR. Depends ONLY on the shared consumer read ports
(GridLayoutPort / CopyRenderSpecPort) and the shared neutral DTOs. It does NOT
import grid_composition or image_variant_management (C-CONSUMER-PORTS / BOM §5.1).

Rule ledger (30-design.md §1 — one enforcement site each):
  - R-01 RenderOrderFollowsPlacementOrder : UC-01 sorts by placement_order (z).
  - R-02 ManualCropOverridesAutoCrop      : UC-02 resolve_effective_crop
                                            (the *application* of IMGVAR R-08).
  - R-03 OnlyResolvableCopiesAreRendered   : UC-01 excludes placements whose
                                            copy spec is None (not an error).
  - R-04 PixelRectComputedFromWeights      : UC-01 cell->pixel via cumulative
                                            weight boundaries (floor rounding).

Contract bindings: identity uuid.UUID (C-IDENTITY); Ok/Err (C-RESULT); not-found
None on the ports (C-REPO-NOTFOUND); UTC tz-aware (not needed here, no
timestamps); enum.Enum for CropKind (C-ENUM); shared synchronous bus (C-EVENTBUS).

MUST_DECIDE_AND_DOCUMENT (RENDERING-local):
  - Pixel rounding policy: floor on cumulative boundaries (30-design §2.3). Using
    cumulative boundaries (not per-cell width sums) guarantees adjacent cells
    abut with no gap/overlap and all rects stay within the canvas.
  - RenderDescriptor dict schema: RenderModel.to_descriptor (domain.py).
  - EffectiveCrop representation: dataclass kind:str + value:Any (domain.py).
"""

from __future__ import annotations

import uuid

from rendering_export import events as ev
from rendering_export.domain import (
    CropKind,
    EffectiveCrop,
    RenderItem,
    RenderModel,
)
from rendering_export.failures import NotFound
from shared.eventbus import RecordingBus
from shared.ports import CopyRenderSpecPort, GridLayoutPort
from shared.render_contracts import CopyRenderSpec, GridLayout, PlacementView
from shared.result import Err, Ok, Result


def _cumulative_boundaries(weights: tuple[int, ...], total_px: int) -> list[int]:
    """R-04 helper. Pixel boundary positions 0..n for the given axis weights.

    boundary[k] = floor(total_px * cumsum(weights)[k] / sum(weights)).
    Cumulative so adjacent cells abut exactly; boundary[0]=0, boundary[n]=total_px.
    """
    n = len(weights)
    total_weight = sum(weights)
    boundaries = [0]
    acc = 0
    for k in range(n):
        acc += weights[k]
        boundaries.append((total_px * acc) // total_weight)
    return boundaries


def resolve_effective_crop(spec: CopyRenderSpec) -> EffectiveCrop:
    """R-02 application (IMGVAR R-08): manual wins over auto; else auto; else none.

    The ONLY enforcement site for R-02. manual and auto may both be present in
    the spec (IMGVAR allows coexistence); when so, manual is used and auto is
    ignored (AT-02). No synthesis of the two is performed (R-02 strict).
    """
    if spec.manual_crop is not None:
        return EffectiveCrop(kind=CropKind.Manual.value, value=spec.manual_crop)
    if spec.auto_crop is not None:
        return EffectiveCrop(kind=CropKind.Auto.value, value=spec.auto_crop)
    return EffectiveCrop(kind=CropKind.NoneKind.value, value=None)


class RenderingExportUseCases:
    def __init__(
        self,
        grid_layout: GridLayoutPort,
        copy_render_spec: CopyRenderSpecPort,
        bus: RecordingBus,
    ) -> None:
        # Ports are stored directly. No adapter wraps either side.
        self._grid_layout = grid_layout
        self._copy_render_spec = copy_render_spec
        self._bus = bus

    # ------------------------------------------------------------------ UC-01
    def build_render_model(
        self, grid_id: uuid.UUID
    ) -> Result[RenderModel, NotFound]:
        built = self._assemble_render_model(grid_id)
        if isinstance(built, Err):
            return built
        model = built.unwrap()
        self._bus.publish(
            ev.RenderModelBuilt(grid_id=grid_id, item_count=len(model.items))
        )
        return Ok(model)

    def _assemble_render_model(
        self, grid_id: uuid.UUID
    ) -> Result[RenderModel, NotFound]:
        """Pure assembly of the RenderModel (R-01/R-03/R-04). No event emitted,
        so UC-01 and UC-03 each control their own single event."""
        layout: GridLayout | None = self._grid_layout.get_grid_layout(grid_id)
        if layout is None:
            return Err(NotFound(entity_kind="Grid", entity_id=grid_id))

        col_bounds = _cumulative_boundaries(layout.col_weights, layout.canvas_w)
        row_bounds = _cumulative_boundaries(layout.row_weights, layout.canvas_h)

        # R-01: placement_order ascending (z order). Sort defensively; the GRID
        # projection already sorts, but RENDERING owns its own z guarantee.
        ordered = sorted(layout.placements, key=lambda p: p.order)

        items: list[RenderItem] = []
        for p in ordered:
            spec = self._copy_render_spec.get_copy_render_spec(p.copy_id)
            if spec is None:
                # R-03: dangling copy reference -> exclude, NOT an error.
                continue
            crop = resolve_effective_crop(spec)            # R-02
            px, py, pw, ph = self._cell_to_pixel(p, col_bounds, row_bounds)  # R-04
            items.append(
                RenderItem(
                    copy_id=p.copy_id,
                    px=px, py=py, pw=pw, ph=ph,
                    effective_crop=crop,
                    scaling_mode=spec.scaling_mode,
                    alignment=spec.alignment,
                    rotation=spec.rotation,
                    flip_x=spec.flip_x,
                    flip_y=spec.flip_y,
                )
            )

        model = RenderModel(
            grid_id=grid_id,
            canvas_w=layout.canvas_w,
            canvas_h=layout.canvas_h,
            items=tuple(items),
        )
        return Ok(model)

    @staticmethod
    def _cell_to_pixel(
        p: PlacementView, col_bounds: list[int], row_bounds: list[int]
    ) -> tuple[int, int, int, int]:
        """R-04: cell rect -> pixel rect using cumulative weight boundaries."""
        px = col_bounds[p.x]
        py = row_bounds[p.y]
        pw = col_bounds[p.x + p.occupy_w] - px
        ph = row_bounds[p.y + p.occupy_h] - py
        return px, py, pw, ph

    # ------------------------------------------------------------------ UC-02
    def resolve_effective_crop(
        self, copy_id: uuid.UUID
    ) -> Result[EffectiveCrop, NotFound]:
        spec = self._copy_render_spec.get_copy_render_spec(copy_id)
        if spec is None:
            return Err(NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        # R-02 application site (delegates to the single module-level function).
        return Ok(resolve_effective_crop(spec))

    # ------------------------------------------------------------------ UC-03
    def export_render_descriptor(
        self, grid_id: uuid.UUID
    ) -> Result[dict, NotFound]:
        built = self._assemble_render_model(grid_id)
        if isinstance(built, Err):
            # Grid NotFound; propagate. No event emitted on failure.
            return built
        model = built.unwrap()
        descriptor = model.to_descriptor()
        self._bus.publish(
            ev.RenderDescriptorExported(grid_id=grid_id, item_count=len(model.items))
        )
        return Ok(descriptor)
