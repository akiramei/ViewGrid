"""RENDERING_EXPORT UseCases (C-UC-CONTAINER: RenderingExportUseCases).

Consumer of GRID + IMGVAR via the shared read ports. Depends ONLY on:
  - shared.ports (GridLayoutPort, CopyRenderSpecPort)
  - shared.render_contracts (GridLayout, PlacementView, CopyRenderSpec)
  - shared.result / shared.events

It must NOT import grid_composition / image_variant_management
(C-CONSUMER-PORTS; verified by tests/test_render_boundary.py).

Rules:
  R-01 RenderOrderFollowsPlacementOrder  (UC-01: sort by order)
  R-02 ManualCropOverridesAutoCrop       (UC-02: IMGVAR R-08 application site)
  R-03 OnlyResolvableCopiesAreRendered   (UC-01: spec None -> exclude, not error)
  R-04 PixelRectComputedFromWeights      (UC-01: cell -> pixel via cumulative weights)
"""
from __future__ import annotations

import uuid

from shared.events import EventBus, NullBus
from shared.ports import CopyRenderSpecPort, GridLayoutPort
from shared.render_contracts import CopyRenderSpec, GridLayout, PlacementView
from shared.result import Err, Ok, Result

from . import events as ev
from . import failures as fail
from .domain import EffectiveCrop, RenderDescriptor, RenderItem, RenderModel


def resolve_effective_crop(spec: CopyRenderSpec) -> EffectiveCrop:
    # R-02: manual present -> manual (ignore auto); else auto; else none.
    if spec.manual_crop is not None:
        return EffectiveCrop(kind="manual", value=spec.manual_crop)
    if spec.auto_crop is not None:
        return EffectiveCrop(kind="auto", value=spec.auto_crop)
    return EffectiveCrop(kind="none", value=None)


def _boundaries(weights: tuple[int, ...], total_px: int) -> list[int]:
    # R-04: cumulative-weight boundaries; floor rounding; gap-free adjacency.
    s = sum(weights)
    bounds = [0]
    cum = 0
    for w in weights:
        cum += w
        bounds.append((total_px * cum) // s)
    return bounds


def cell_to_pixel(p: PlacementView, layout: GridLayout) -> tuple[int, int, int, int]:
    # R-04: convert cell rect to pixel rect using weights within the canvas.
    col_b = _boundaries(layout.col_weights, layout.canvas_w)
    row_b = _boundaries(layout.row_weights, layout.canvas_h)
    px = col_b[p.x]
    py = row_b[p.y]
    pw = col_b[p.x + p.occupy_w] - px
    ph = row_b[p.y + p.occupy_h] - py
    return px, py, pw, ph


class RenderingExportUseCases:
    def __init__(
        self,
        grid_layout: GridLayoutPort,
        copy_render_spec: CopyRenderSpecPort,
        bus: EventBus | None = None,
    ) -> None:
        self._grid_layout = grid_layout
        self._copy_render_spec = copy_render_spec
        self._bus = bus or NullBus()

    # ------------------------------------------------------------------
    # UC-01 BuildRenderModel
    # ------------------------------------------------------------------
    def build_render_model(self, grid_id: uuid.UUID) -> Result[RenderModel, object]:
        layout = self._grid_layout.get_grid_layout(grid_id)
        if layout is None:
            return Err(fail.NotFound(entity_kind="Grid", entity_id=grid_id))
        # R-01: z-order ascending by placement_order.
        ordered = sorted(layout.placements, key=lambda p: p.order)
        items: list[RenderItem] = []
        for p in ordered:
            spec = self._copy_render_spec.get_copy_render_spec(p.copy_id)
            if spec is None:
                # R-03: dangling reference -> exclude, not an error.
                continue
            crop = resolve_effective_crop(spec)  # R-02
            px, py, pw, ph = cell_to_pixel(p, layout)  # R-04
            items.append(
                RenderItem(
                    copy_id=p.copy_id,
                    px=px,
                    py=py,
                    pw=pw,
                    ph=ph,
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
        self._bus.publish(ev.RenderModelBuilt(grid_id=grid_id, item_count=len(items)))
        return Ok(model)

    # ------------------------------------------------------------------
    # UC-02 ResolveEffectiveCrop
    # ------------------------------------------------------------------
    def resolve_effective_crop(self, copy_id: uuid.UUID) -> Result[EffectiveCrop, object]:
        spec = self._copy_render_spec.get_copy_render_spec(copy_id)
        if spec is None:
            return Err(fail.NotFound(entity_kind="ImageCopy", entity_id=copy_id))
        return Ok(resolve_effective_crop(spec))  # R-02

    # ------------------------------------------------------------------
    # UC-03 ExportRenderDescriptor
    # ------------------------------------------------------------------
    def export_render_descriptor(self, grid_id: uuid.UUID) -> Result[RenderDescriptor, object]:
        model_res = self.build_render_model(grid_id)
        if isinstance(model_res, Err):
            return model_res
        descriptor = RenderDescriptor.from_model(model_res.value)
        self._bus.publish(
            ev.RenderDescriptorExported(grid_id=grid_id, item_count=len(descriptor.items))
        )
        return Ok(descriptor)
