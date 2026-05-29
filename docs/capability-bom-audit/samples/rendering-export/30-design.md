# 30 — 設計書 (RENDERING_EXPORT, focused v0.1)

## 1. Rule Ledger

| ID | Rule | 保証場所 (唯一) | 検証 |
| --- | --- | --- | --- |
| R-01 | RenderOrderFollowsPlacementOrder | UC-01 `BuildRenderModel` で `sorted(placements, key=order)` | AT-01 |
| R-02 | ManualCropOverridesAutoCrop | UC-02 `ResolveEffectiveCrop` (IMGVAR R-08 の適用点) | AT-02/03/04 |
| R-03 | OnlyResolvableCopiesAreRendered | UC-01 で spec=None の placement を除外 | AT-07 |
| R-04 | PixelRectComputedFromWeights | UC-01 のセル→ピクセル変換 | AT-05/06 |

> **NOTE (R-02)**: manual と auto が **両方存在** しうる (IMGVAR は共存を許す = R-08 宣言のみ)。
> その場合 **manual を採用し auto を無視** する。これは「片方しか無い」前提で書くと取りこぼす
> エッジ。narrative (本 NOTE) + algorithmic (§2.2) + executable (AT-02) の三層で固定する。

## 2. workflow_decision

### 2.1 UC-01 BuildRenderModel の手順

```text
(i)   layout = GridLayoutPort.get_grid_layout(grid_id)
(ii)  layout is None -> Err(NotFound(entity_kind="Grid", entity_id=grid_id))
(iii) placements を placement_order 昇順に整列 (R-01)
(iv)  各 placement p について:
        spec = CopyRenderSpecPort.get_copy_render_spec(p.copy_id)
        spec is None -> この placement を *除外* (R-03、エラーにしない)
        crop = resolve_effective_crop(spec)          # R-02、§2.2
        (px,py,pw,ph) = cell_to_pixel(p, layout)     # R-04、§2.3
        RenderItem(copy_id=p.copy_id, px,py,pw,ph, crop, scaling, alignment, rotation, flips)
(v)   RenderModel(grid_id, canvas_w, canvas_h, items=z 順)
(vi)  publish RenderModelBuilt(grid_id, item_count)
```

### 2.2 R-02 適用アルゴリズム (resolve_effective_crop)

```text
if spec.manual_crop is not None:
    return EffectiveCrop(kind="manual", value=spec.manual_crop)   # auto は無視
elif spec.auto_crop is not None:
    return EffectiveCrop(kind="auto", value=spec.auto_crop)
else:
    return EffectiveCrop(kind="none", value=None)
```

### 2.3 R-04 セル→ピクセル変換 (cell_to_pixel)

```text
col 境界 = canvas_w * (cumsum(col_weights)[i] / sum(col_weights))   for i in 0..grid_cols
row 境界 = canvas_h * (cumsum(row_weights)[j] / sum(row_weights))   for j in 0..grid_rows
px = col_boundary[p.x]
py = row_boundary[p.y]
pw = col_boundary[p.x + p.occupy_w] - px
ph = row_boundary[p.y + p.occupy_h] - py
(整数丸めは floor。境界は累積で計算し、隣接セルが隙間なく接するようにする)
```

## 3. Worked Examples

- **W-1 (z 順)**: 3 placements order=[3,1,2] → items は order 1,2,3 の順。
- **W-2 (R-02 manual 優先)**: spec.manual_crop=(0.1,0.1,0.5,0.5), spec.auto_crop=(0xFFFFFFFF,10)
  → EffectiveCrop(kind="manual", (0.1,0.1,0.5,0.5))。auto は無視。
- **W-3 (R-02 auto)**: manual=None, auto=(0xFF000000,5) → kind="auto"。
- **W-4 (R-02 none)**: manual=None, auto=None → kind="none"。
- **W-5 (R-04 uniform)**: grid 2x2, weights [1,1]/[1,1], canvas 100x100, placement (x=0,y=0,1x1)
  → (px,py,pw,ph)=(0,0,50,50)。placement (x=1,y=1,1x1) → (50,50,50,50)。
- **W-6 (R-04 non-uniform)**: grid 1x2 (cols), col_weights [1,3], canvas 100x?, placement (x=0,1x1)
  → px=0,pw=25; placement (x=1,1x1) → px=25,pw=75。
- **W-7 (R-03 dangling)**: placement の copy spec=None → render model から除外。残りは描画。

## 4. 必須テストカテゴリ (§6.1 相当)

1. Rule 単体: R-01/R-02/R-03/R-04 を独立に
2. UC happy / failure: UC-01/UC-02/UC-03 の成功と NotFound
3. Event: RenderModelBuilt / RenderDescriptorExported が成功時のみ発火
4. Anchor tests AT-01..AT-08 (§5)
5. **Property-based (1000-step random walk)**: ランダムな grid/placement/copy 設定で
   不変条件 (items が z 順 / 全 item の px,py,pw,ph >= 0 かつ canvas 内 / crop kind が 3 値) を確認
6. **境界**: RENDERING が GRID/IMGVAR の domain 型を import していないことを静的に確認するテスト

## 5. Anchor Tests (AT-01..AT-08)

| AT | 内容 | 関連 Rule |
| --- | --- | --- |
| AT-01 | order=[3,1,2] の placements → items が order 昇順 | R-01 |
| AT-02 | manual+auto 両方 → EffectiveCrop kind="manual" (auto 無視) | R-02 |
| AT-03 | auto のみ → kind="auto" | R-02 |
| AT-04 | 両方 None → kind="none" | R-02 |
| AT-05 | 2x2 uniform, canvas 100x100, (0,0,1x1) → (0,0,50,50) | R-04 |
| AT-06 | 1x2 cols weights [1,3], canvas 100 幅, (x=1,1x1) → px=25,pw=75 | R-04 |
| AT-07 | dangling copy (spec=None) → render model から除外、他は残る | R-03 |
| AT-08 | 存在しない grid_id で UC-01 → NotFound(entity_kind="Grid") | — |

> AT-02..AT-04 が R-02 (= IMGVAR R-08 の適用) を executable 層で固定する。
> AI が「manual と auto を合成」しようとする局所最適化を AT-02 が捕捉する。

## 6. 境界実装の指針 (C-CONSUMER-PORTS)

- RENDERING は `from shared.ports import GridLayoutPort, CopyRenderSpecPort` と
  `from shared.render_contracts import GridLayout, PlacementView, CopyRenderSpec` のみ。
- **`from grid_composition...` / `from image_variant_management...` を import しない**。
- producer 側 (既存 n=2 実装) には `get_grid_layout` / `get_copy_render_spec` を **native projection** として追加してよい (standalone アダプタは禁止)。
