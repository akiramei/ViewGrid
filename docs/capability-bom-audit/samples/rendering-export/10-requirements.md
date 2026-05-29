# 10 — 要求仕様 (RENDERING_EXPORT, focused v0.1)

> **Scope: focused**。n=3 スケール検証 (Addendum G) 用に、**cross-Capability の read 面に集中** した
> 最小の RENDERING_EXPORT。PhotoBoard/Normal の 2 モードや実ファイル出力 (PNG/SkiaSharp 等) は
> **対象外** (full 版は将来)。本書は「グリッド配置 + ImageCopy 設定を読み、描画モデルを構築する」
> 部分のみを規定する。

## 1. 目的

GRID_COMPOSITION が定めた **配置 (Placement)** と、IMAGE_VARIANT_MANAGEMENT が定めた
**ImageCopy の見え方設定 (transform / scaling / alignment / crop)** を読み取り、
**描画モデル (RenderModel)** を構築する。

RENDERING_EXPORT は **両 Capability の消費側 (consumer)** であり、両者の状態を **read するのみ**。
配置や copy 設定を変更しない。

## 2. ユースケースシナリオ

- **S1**: 編集者が 1 つのグリッドを「プレビュー」する。RENDERING はそのグリッドの全 Placement を
  placement_order の z 順に並べ、各 copy の見え方を解決した描画モデルを返す。
- **S2**: 各 copy の **有効クロップ** は「手動クロップがあればそれを優先、なければ自動クロップ、
  どちらも無ければクロップ無し」で決まる (= IMAGE_VARIANT の R-08 の **適用**)。
- **S3**: 各 Placement のセル矩形は、グリッドの行/列ウェイトに比例して **ピクセル矩形** に変換される
  (canvas_size 内)。
- **S4**: 描画モデルを **シリアライズ可能な記述子 (descriptor)** に書き出す (export の最小形)。

## 3. 入力 (cross-Capability read)

| 読む対象 | 提供 Capability | 取得手段 (契約) |
| --- | --- | --- |
| グリッド幾何 + 配置一覧 | GRID_COMPOSITION | `GridLayoutPort.get_grid_layout(grid_id)` → `GridLayout` |
| copy の見え方設定 | IMAGE_VARIANT_MANAGEMENT | `CopyRenderSpecPort.get_copy_render_spec(copy_id)` → `CopyRenderSpec` |

いずれも `00-convention-contract.md §1.8 C-CONSUMER-PORTS` の **中立 DTO** で受け取る。
RENDERING は GRID / IMGVAR の **domain 型を import しない**。

## 4. 非機能 / 制約

- RENDERING は read 専用。GRID / IMGVAR の状態を変更しない。
- R-08 (ManualCropOverridesAutoCrop) の **唯一の適用点** は本 Capability (IMGVAR は宣言のみ)。
- 描画モデルは決定的 (同じ入力 → 同じ出力)。

## 5. 用語集 (本 Capability 固有)

| 語 | 意味 |
| --- | --- |
| RenderModel | グリッド 1 つに対する、z 順の描画アイテム列 + キャンバス情報 |
| RenderItem | 1 つの Placement に対応する描画単位 (copy_id, ピクセル矩形, 有効クロップ, scaling, alignment, transform) |
| EffectiveCrop | R-08 適用後の有効クロップ (`manual` / `auto` / `none` のいずれか) |
| RenderDescriptor | RenderModel のシリアライズ可能形 (dict) |

> 共有語 `OccupySize` / `PixelSize` は GRID_COMPOSITION と共有 (用語の権威は GRID 側)。
> 本 Capability はセル座標・占有を `GridLayout` / `PlacementView` 経由で受け取る。
