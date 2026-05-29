# 20 — Capability BOM (IMAGE_VARIANT_MANAGEMENT)

> **Version: v0.1**

## 1. Capability の同定

| 項目 | 値 |
| --- | --- |
| **ID** | `IMAGE_VARIANT_MANAGEMENT` |
| **Name (日)** | 画像派生物管理 |
| **Name (en)** | Image Variant Management |
| **Layer (意味)** | Domain Capability |
| **Volatility** | 中 (Transform / Crop の値域は安定。UC は機能追加で増える可能性) |
| **Stakeholder** | 編集者 / 大量再利用者 / 整理志向ユーザー |

### Purpose

> 1 枚の元画像 (`ImageAsset`) に対し、設定違いの **論理コピー (`ImageCopy`)** を
> 複数生成・編集・問い合わせする機能を提供する。
> `ImageCopy` の **意味的権威** (どんな設定が許され、どう優先されるか) は本 Capability にある。

---

## 2. UseCases

| ID | 名前 | 種別 | 失敗理由 |
| --- | --- | --- | --- |
| UC-01 | `ImportImageAsset` | command | InvalidImageData / UnsupportedMimeType |
| UC-02 | `DeleteImageAsset` | command | NotFound / DependentCopiesExist |
| UC-03 | `ListImageAssets` | query | (none) |
| UC-04 | `GetImageAsset` | query | NotFound |
| UC-05 | `CreateImageCopy` | command | NotFound / InvalidAlignment / InvalidScalingMode / InvalidOccupySize / InvalidTransform / InvalidCopyName |
| UC-06 | `DeleteImageCopy` | command | NotFound |
| UC-07 | `ListImageCopies` | query | (none、AssetId 未指定なら全件、指定なら絞り込み) |
| UC-08 | `GetImageCopy` | query | NotFound |
| UC-09 | `ChangeCopyTransform` | command | NotFound / InvalidTransform |
| UC-10 | `ChangeScalingMode` | command | NotFound / InvalidScalingMode |
| UC-11 | `ChangeAlignment` | command | NotFound / InvalidAlignment |
| UC-12 | `ChangeAutoCropSettings` | command | NotFound / InvalidAutoCropSettings |
| UC-13 | `ChangeManualCropSettings` | command | NotFound / InvalidManualCropFractions |
| UC-14 | `ChangeDefaultOccupySize` | command | NotFound / InvalidOccupySize |
| UC-15 | `RenameImageCopy` | command | NotFound |
| UC-16 | `ImageCopyExists` | query | (none、存在しなければ false) |
| UC-17 | `ImageAssetExists` | query | (none) |

### 2.1 失敗理由 `NotFound` の規範

GRID_COMPOSITION v0.2 と同じ規範を採用する:

- Payload: `{ entity_kind: "ImageAsset" | "ImageCopy", entity_id: identity }`
- 前提条件破れの正準失敗理由

### 2.2 失敗理由 `DependentCopiesExist` の意味 (本 Capability 固有)

UC-02 で `ImageAsset` 削除時、関連する `ImageCopy` が 1 件以上ある場合に返す。

- Payload: `{ asset_id: identity, dependent_copy_ids: [identity] }`
- **本 Capability は自動カスケード削除を行わない**。呼び出し側に判断を委ねる
- 上位 Coordinator が "deleted cascade" を望む場合は、先に `ImageCopy` 群を `DeleteImageCopy` で消し、その後 `DeleteImageAsset` を呼ぶ

---

## 3. Rules

| Rule ID | 制約内容 | 種別 | 保証場所 |
| --- | --- | --- | --- |
| R-01 | `ImageAssetMustHaveValidImageData` | invariant | UseCase 層 (UC-01 入力検証) |
| R-02 | `ImageAssetFileHashMustBeUnique` | invariant | UseCase 層 (UC-01 で hash 重複を検出して既存返却) |
| R-03 | `ImageCopyMustReferenceExistingAsset` | invariant | UseCase 層 + Domain (FK の意味整合) |
| R-04 | `ScalingModeMustBeFromEnumeratedSet` | invariant | Domain (Enum 制約) |
| R-05 | `AlignmentMustBeFromEnumeratedSet` | invariant | Domain (Enum 制約) |
| R-06 | `AutoCropSettingsAreBothOrNeither` | invariant | Domain (集約値オブジェクトの構築時) |
| R-07 | `ManualCropFractionsMustBeNormalized` | invariant | Domain (構築時) |
| R-08 | `ManualCropOverridesAutoCrop` | semantic | **本 Capability では宣言のみ。実際の適用は `RENDERING_EXPORT` が担う** |
| R-09 | `RotationMustBeMultipleOf90` | invariant | Domain (Rotation Enum: None/CW90/CW180/CW270) |
| R-10 | `DefaultOccupySizeMustBePositive` | invariant | Domain (OccupySize.width ≥ 1, height ≥ 1) |
| R-11 | `ImageCopyNameMustBeNullOrNonEmpty` | invariant | Domain (CopyName が空文字列 `""` であってはならない。null は許容) |

### 3.1 Rule R-08 の特殊性 (Capability 境界に跨る Rule)

`ManualCropOverridesAutoCrop` は本 Capability が **保持** するが **適用** はしない。

- **本 Capability の責任**: AutoCrop と ManualCrop の **両方が同時に設定された状態を許容する** (片方を上書きしない)
- **RENDERING_EXPORT の責任**: 描画時にどちらが優先されるかの **意味解釈と適用**

> [!IMPORTANT]
> これは v0.2 で確立した **「Capability 境界に跨る意味」** の典型例。
> 本 Capability の Rule ledger には記載するが、保証コードは別 Capability に置く。
> Phase 2 試行時は、AI が両方の値を保存できることだけテストし、優先関係そのものの
> テストは RENDERING_EXPORT が用意される時に追加する。

---

## 4. Entities

### 4.1 所有 (Owned)

| Entity | 説明 | 不変条件 |
| --- | --- | --- |
| `ImageAsset` | 元画像メタデータ | R-01, R-02 |
| `ImageCopy` | 論理コピー (派生物) | R-03 〜 R-11 |

### 4.2 参照のみ (Referenced)

なし (本 Capability は他 Capability の Entity を持たない)

### 4.3 値オブジェクト

| Value Object | 意味 |
| --- | --- |
| `PixelSize` | 元画像のピクセルサイズ (GRID_COMPOSITION と共有定義) |
| `OccupySize` | 配置時の既定占有セル数 (GRID_COMPOSITION と共有定義) |
| `ImageTransform` | 回転 (Rotation) + FlipX + FlipY |
| `AutoCropSettings` | TargetColor (UInt32 ARGB) + Threshold (0-255)、両値で意味あり |
| `ManualCropFraction` | (X, Y, Width, Height) すべて [0.0, 1.0]、合計範囲は元画像座標系 |

> [!IMPORTANT]
> `PixelSize` / `OccupySize` は GRID_COMPOSITION の値オブジェクトと **同じ型**。
> 二重定義しない。これは **境界調整負荷の最たる例** (詳細は ../../evaluation/90-feasibility-notes.md Addendum C)。

### 4.4 Enum 値の規範

| Enum | 値集合 |
| --- | --- |
| `Rotation` | `None` (0°) / `CW90` (90°) / `CW180` (180°) / `CW270` (270°) |
| `ScalingMode` | `UniformContain` / `UniformCover` / `Fill` |
| `Alignment` | 9 点 (上下 3 × 左右 3): TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight |

---

## 5. Events

| Event | 発生条件 | 主な購読者 |
| --- | --- | --- |
| `ImageAssetImported` | UC-01 成功時 (新規) | HISTORY、永続化、UI |
| `ImageAssetImportedAsDuplicate` | UC-01 で hash 重複により既存返却時 | UI (通知) |
| `ImageAssetDeleted` | UC-02 成功時 | HISTORY、永続化、関連 Capability |
| `ImageCopyCreated` | UC-05 成功時 | HISTORY、UI、GRID_COMPOSITION (再評価) |
| `ImageCopyDeleted` | UC-06 成功時 | HISTORY、**GRID_COMPOSITION (cascade 判定の根拠)**、UI |
| `ImageCopyTransformChanged` | UC-09 | HISTORY、RENDERING_EXPORT (再描画) |
| `ImageCopyScalingModeChanged` | UC-10 | HISTORY、RENDERING |
| `ImageCopyAlignmentChanged` | UC-11 | HISTORY、RENDERING |
| `ImageCopyAutoCropChanged` | UC-12 | HISTORY、RENDERING |
| `ImageCopyManualCropChanged` | UC-13 | HISTORY、RENDERING |
| `ImageCopyDefaultOccupySizeChanged` | UC-14 | HISTORY |
| `ImageCopyRenamed` | UC-15 | HISTORY、UI |

> `ImageCopyDeleted` イベントは **GRID_COMPOSITION が購読すべき重要イベント**。
> ただし「削除時の Placement への影響をどうするか」は GRID_COMPOSITION 側の責任
> (本 Capability は知らない)。

---

## 6. Decision Ownership

| Decision 種別 | 所在 | 例 |
| --- | --- | --- |
| `domain_decision` | UseCase + Domain Model | `ImageCopy` の設定の有効性、AutoCrop の両値性 |
| `validation_decision` | UseCase 層 | 入力値の範囲・型 |
| `workflow_decision` | UseCase 層 | UC-01 の hash 重複検出フロー |
| `ui_interaction_decision` | 上位層 | 出力外 |
| `persistence_decision` | Repository 層 | 出力外 (ファイル保存形式は AI 任意) |
| `rendering_decision` | **RENDERING_EXPORT** | **AutoCrop vs ManualCrop の優先 (R-08)** |
| `history_decision` | HISTORY_MANAGEMENT | 出力外 |
| `cascade_decision` | **上位 Coordinator** | **UC-02 で派生物がある時の挙動。本 Capability は拒否のみ** |

### 6.1 重要な禁則

- **UC-02 が自動でカスケード削除してはならない**: 純度を保つため、依存があれば拒否する
- **本 Capability が AutoCrop / ManualCrop の優先を適用してはならない**: 保持のみ
- **本 Capability が画像の物理保存形式を決めてはならない**: Repository 任意
- **本 Capability が描画のための前計算 (サムネ等) を持ってはならない**: RENDERING_EXPORT へ

---

## 7. Role Taxonomy

### 7.1 Allowed

| Role | 担当層 | 例 |
| --- | --- | --- |
| `observes` | UI 層 | `ImageCopyChanged` 系イベント購読 |
| `projects` | UI 層 | サムネ表示用射影 |
| `invokes` | UI / 上位 Coordinator | UC 呼び出し |
| `coordinates` | 上位 Coordinator | UC-02 cascade 判断、cross-Capability 整合 |
| `enforces` | UseCase 層 | R-01〜R-07, R-09〜R-11 |
| `owns` | UseCase + Domain | `ImageCopy` の状態と Decision 所有 |
| `persists` | Repository 層 | `ImageAsset` 物理保存 + `ImageCopy` 設定永続化 |

### 7.2 Suspicious / Forbidden

| 状況 | 判定 | 理由 |
| --- | --- | --- |
| 本 Capability が AutoCrop vs ManualCrop の優先を適用 | **Forbidden** | rendering_decision の越境 |
| 本 Capability が `Placement` を参照 | **Forbidden** | 逆依存 (GRID_COMPOSITION → 本 Capability の関係を保つ) |
| UC-02 が依存派生物を自動削除 | **Forbidden** | cascade_decision を勝手に所有 |
| RENDERING_EXPORT が `ImageCopy` の特性を直接書き換える | **Forbidden** | 本 Capability の権威越境 |

---

## 8. Capability Boundaries

```text
                 ┌─────────────────────────┐
                 │ WORKSPACE_MANAGEMENT    │
                 │  (DB / Storage 切替)    │
                 └───────────┬─────────────┘
                             │ provides storage
                             ▼
        ┌────────────────────────────────────────┐
        │  IMAGE_VARIANT_MANAGEMENT              │
        │   (本 Capability)                      │
        │  - ImageAsset / ImageCopy の権威       │
        │  - 設定の保持と検証                    │
        │  - hash 重複除去                       │
        └────┬──────────────────────┬────────────┘
             │ emits events         │ provides existence check
             ▼                      ▼
   ┌──────────────────┐   ┌──────────────────────┐
   │ HISTORY_MANAGE   │   │  GRID_COMPOSITION    │
   │     MENT         │   │  (CopyId のみ参照)   │
   └──────────────────┘   └──────────────────────┘
                            │
                            ▼ subscribes to
              ┌─────────────────────────────────┐
              │ RENDERING_EXPORT                 │
              │  (ImageCopy の特性を描画解釈)    │
              │  - AutoCrop vs ManualCrop の優先 │
              │  - Transform / Scaling の適用    │
              └─────────────────────────────────┘
```

### 8.1 GRID_COMPOSITION との境界

- 本 Capability が **権威**: `ImageCopy` の概念 / 設定 / 存在性
- GRID_COMPOSITION が **権威**: `Placement` の配置論理 / グリッド境界
- 接続点: `ImageCopyExists` (UC-16) ── GRID_COMPOSITION の UC-05 が呼ぶ
- 削除カスケード: `ImageCopyDeleted` イベントを GRID_COMPOSITION が購読し、関連 Placement を削除するかを **GRID_COMPOSITION 側で決定** (本 Capability は知らない)

### 8.2 RENDERING_EXPORT との境界

- 本 Capability が **権威**: `ImageCopy` の特性データの保持
- RENDERING_EXPORT が **権威**: その特性をどう適用して描画するか (R-08 の優先関係を含む)

### 8.3 WORKSPACE_MANAGEMENT との境界

- 本 Capability は `IImageStorage` / Repository インターフェースを **使う** のみ
- 実体ファイルの保存場所 / DB スキーマは本 Capability 外

---

## 9. 観測可能性 (監査要件)

- 各 UseCase は入力 → 結果の単一関数として表現可能
- 各 Rule R-01〜R-07, R-09〜R-11 はコード上 1 箇所で保証
- R-08 (ManualCropOverridesAutoCrop) は **本 Capability 内では「設定の共存を許す」テストのみ** 持ち、実際の優先関係テストは RENDERING_EXPORT 側で行う

---

## 10. 関連ドキュメント

- `21-image-variant-management.yaml` — 機械可読版 (正準)
- `30-design.md` — Rule ledger / Entity 意味 / テスト規範
- `40-ai-implementation-prompt.md` — AI 実装プロンプト雛形
- `../README.md` — 親ディレクトリ (GRID_COMPOSITION サンプル)
- `../../evaluation/90-feasibility-notes.md` Addendum C — 境界調整負荷の観測
