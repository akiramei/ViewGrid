# 20 — Capability BOM (RENDERING_EXPORT, focused v0.1)

> 意思決定の所在と境界を述べる。機械可読の正準は `21-rendering-export.yaml` (矛盾時は YAML が正)。

## 1. Capability 概要

| 項目 | 値 |
| --- | --- |
| **ID** | RENDERING_EXPORT |
| **Name (日)** | 描画・出力 |
| **Layer** | Domain Capability (consumer) |
| **Stakeholder** | 編集者 / 大判出力者 |

### Purpose

> GRID_COMPOSITION の配置と IMAGE_VARIANT_MANAGEMENT の ImageCopy 設定を **read** し、
> z 順・有効クロップ・ピクセル幾何を解決した **描画モデル** を構築する。
> 描画の見え方に関する **唯一の権威** であり、ManualCropOverridesAutoCrop の **適用者** である。

## 2. UseCases

| ID | Name | 種別 | 失敗理由 |
| --- | --- | --- | --- |
| UC-01 | `BuildRenderModel` | query | NotFound |
| UC-02 | `ResolveEffectiveCrop` | query | NotFound |
| UC-03 | `ExportRenderDescriptor` | command | NotFound |

### 2.1 canonical_failure_reasons

- **NotFound**: `{ entity_kind: "Grid" | "ImageCopy", entity_id: identity }`
  - UC-01 / UC-03: `grid_id` に対する `GridLayoutPort.get_grid_layout` が `None` → `NotFound(entity_kind="Grid")`
  - UC-02: `copy_id` に対する `CopyRenderSpecPort.get_copy_render_spec` が `None` → `NotFound(entity_kind="ImageCopy")`
  - **UC-01 内で個々の placement の copy spec が `None` の場合は失敗ではなく除外** (R-03、dangling 参照は描画しない)

## 3. Rules

| ID | Name | 種別 | 保証場所 |
| --- | --- | --- | --- |
| R-01 | `RenderOrderFollowsPlacementOrder` | invariant | UseCase (UC-01) — placement_order 昇順で z 整列 |
| R-02 | `ManualCropOverridesAutoCrop` | policy | UseCase (UC-02) — **IMGVAR R-08 の適用点** |
| R-03 | `OnlyResolvableCopiesAreRendered` | invariant | UseCase (UC-01) — spec が `None` の placement は除外 |
| R-04 | `PixelRectComputedFromWeights` | invariant | UseCase (UC-01) — 行/列ウェイト比例でセル→ピクセル変換 |

### 3.1 R-02 の特殊性 (cross-Capability Rule の適用側)

`ManualCropOverridesAutoCrop` は IMAGE_VARIANT_MANAGEMENT の Rule ledger に **R-08 として宣言** されているが
**保証コードは持たない** (Declaration-only Rule)。本 Capability が **適用者** である:

> manual_crop が非 None → manual を採用 (auto は無視) /
> manual_crop が None かつ auto_crop が非 None → auto を採用 /
> 両方 None → クロップ無し

これは三層構造で固定する (narrative=本節 / algorithmic=30-design §2.2 / executable=AT-02..AT-04)。

## 4. Domain / 値オブジェクト

本 Capability の出力型 (RENDERING ローカル):

| 型 | 内容 |
| --- | --- |
| `RenderModel` | `{ grid_id, canvas_w, canvas_h, items: [RenderItem...] }` (items は z 順) |
| `RenderItem` | `{ copy_id, px, py, pw, ph, effective_crop, scaling_mode, alignment, rotation, flip_x, flip_y }` |
| `EffectiveCrop` | `{ kind: "manual"|"auto"|"none", value: ... }` |
| `RenderDescriptor` | `RenderModel` の dict 形 (シリアライズ可能) |

**入力は shared 中立 DTO** (`GridLayout` / `PlacementView` / `CopyRenderSpec`)。
RENDERING は GRID の `Placement` / IMGVAR の `ImageCopy` を **import しない** (C-CONSUMER-PORTS)。

## 5. Decision ownership

| Decision | 所有 | 説明 |
| --- | --- | --- |
| `rendering_decision` | RENDERING UseCase | z 順 / 有効クロップ解決 (R-02) / ピクセル幾何 (R-04) |
| `domain_decision` (配置妥当性) | **GRID_COMPOSITION** | 本 Capability は read のみ。再判定しない |
| `domain_decision` (copy 設定の妥当性) | **IMAGE_VARIANT_MANAGEMENT** | 本 Capability は read のみ。crop 値の妥当性は IMGVAR が保証済み |

### 5.1 Forbidden

| 行為 | 判定 | 理由 |
| --- | --- | --- |
| Placement / ImageCopy を変更する | **Forbidden** | read 専用の consumer |
| GRID R-01/R-02 (配置妥当性) を再判定する | **Forbidden** | GRID の権威 |
| crop 値の妥当性 (R-06/R-07) を再検証する | **Forbidden** | IMGVAR が保証済み。RENDERING は **適用** のみ |
| GRID / IMGVAR の domain 型を import する | **Forbidden** | C-CONSUMER-PORTS (中立 DTO のみ) |

## 6. Events

| Event | 発行 | payload |
| --- | --- | --- |
| `RenderModelBuilt` | UC-01 成功時 | `{ grid_id, item_count }` |
| `RenderDescriptorExported` | UC-03 成功時 | `{ grid_id, item_count }` |

## 7. Capability Boundaries

```text
   GRID_COMPOSITION ──(GridLayoutPort.get_grid_layout)──┐
                                                        ▼
                                              RENDERING_EXPORT ──> RenderModel / Descriptor
                                                        ▲
   IMAGE_VARIANT_MANAGEMENT ──(CopyRenderSpecPort.get_copy_render_spec)──┘
```

### 7.1 GRID_COMPOSITION との境界
- 本 Capability は `GridLayoutPort` を通じて **グリッド幾何 + 配置一覧** を read する。
- GRID 側は `GridCompositionUseCases.get_grid_layout(grid_id) -> GridLayout | None` を **native に満たす**。

### 7.2 IMAGE_VARIANT_MANAGEMENT との境界
- 本 Capability は `CopyRenderSpecPort` を通じて **copy の見え方設定** を read する。
- IMGVAR 側は `ImageVariantManagementUseCases.get_copy_render_spec(copy_id) -> CopyRenderSpec | None` を **native に満たす**。
- **R-08 (ManualCropOverridesAutoCrop) の適用は本 Capability** (IMGVAR は宣言のみ)。

> いずれの境界も `00-convention-contract.md §1.8 C-CONSUMER-PORTS` に従い **standalone アダプタ禁止 / 中立 DTO のみ**。
