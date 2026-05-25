# 30 — 設計書 (IMAGE_VARIANT_MANAGEMENT)

> **Version: v0.1** (GRID_COMPOSITION v0.2 で確立した規範を初回から適用)

## このドキュメントの位置づけ

本書は `IMAGE_VARIANT_MANAGEMENT` の **設計の意味的詳細** を述べる。

実装方針 (クラス分割・ファイル配置) は AI 任意。本書が固定するのは
「何を保証するか」「何を意味するか」のみ。

---

## 1. Rule Ledger

> 各 Rule の保証場所と検証アルゴリズム。R-08 だけは本 Capability では宣言のみ。

### R-01: ImageAssetMustHaveValidImageData

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | UseCase 層 (UC-01 入口) |
| Verification | バイト列を画像 decoder に通し、サイズを取得できることを確認 |
| 失敗時 | `InvalidImageData(detail)` |

### R-02: ImageAssetFileHashMustBeUnique

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | UseCase 層 (UC-01 のフロー) |
| Verification | SHA-256 を計算し、既存 `ImageAsset` の hash と一致する場合は新規生成せず既存を返す |
| 失敗時 | 失敗ではない (`ImageAssetImportedAsDuplicate` イベントを発行する成功扱い) |

**重要**: DB のユニーク制約に委ねない (cascade_decision の §6 参照)。
本 Capability が hash 一覧をクエリして判定する。

### R-03: ImageCopyMustReferenceExistingAsset

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | UseCase 層 + Domain Model |
| Verification | UC-05 で `asset_id` を `ImageAssetExists` で確認。Domain Model 構築時に `asset_id != null` |
| 失敗時 | `NotFound(entity_kind="ImageAsset", entity_id=asset_id)` |

> [!IMPORTANT]
> Domain Model 上は **FK 整合性まで保証しない** (DB レベルでは別の責任)。
> R-03 の意味は「`ImageCopy` の `asset_id` フィールドが必ず非 null かつ Asset を参照する意図を持つ」レベル。
> 物理的に Asset が消えた後の `ImageCopy` の処理は cascade_decision の領域。

### R-04, R-05: Enum 集合からの値

`ScalingMode` / `Alignment` / `Rotation` はそれぞれ列挙値集合から選ばれる。
構築時に Enum 制約で保証 (R-09 も含む)。

### R-06: AutoCropSettingsAreBothOrNeither

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | Domain Model (集約値オブジェクト `AutoCropSettings` の構築時) |
| Verification | `target_color_argb` と `threshold` の **両値とも非 null** か **両値とも null** |
| 失敗時 | UseCase 層で `InvalidAutoCropSettings(detail, ...)` |

`ImageCopy` に保持する形は **`AutoCropSettings? auto_crop`** (集約値が null か非 null か)。
EF Core 等の永続化で `target_color` と `threshold` の 2 つの primitive フィールドに分解する場合も、
**集約値オブジェクトとして再構築するときに R-06 を保証する**。

### R-07: ManualCropFractionsMustBeNormalized

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | Domain Model (`ManualCropFraction` 構築時) |
| Verification | 4 値とも null か 4 値とも非 null。非 null なら各値 ∈ [0.0, 1.0] かつ `x + width ≤ 1.0`、`y + height ≤ 1.0` |
| 失敗時 | UseCase 層で `InvalidManualCropFractions(detail, ...)` |

### R-08: ManualCropOverridesAutoCrop (Capability 境界に跨る Rule)

| 項目 | 内容 |
| --- | --- |
| Kind | semantic |
| Owned by | **RENDERING_EXPORT (別 Capability)** |
| 本 Capability の責任 | 両方の値を保存し、両者が共存できる状態を許容する |
| RENDERING_EXPORT の責任 | 描画時にどちらを優先するかの解釈と適用 |

> [!IMPORTANT]
> **本 Capability の Rule ledger に記載するのは「この Rule の存在を示すため」のみ**。
> 保証コードは本 Capability に書かない。
> Anchor test も「両方の値が共存できること」だけを確認する (AT-04 参照)。

### R-09 〜 R-11

R-09 (Rotation 列挙)、R-10 (OccupySize 正値)、R-11 (CopyName null か非空) は
すべて Domain Model の構築時で保証。詳細は前述。

---

## 2. Decision Specification

### 2.1 workflow_decision の詳細

| 操作 | 内部手順 |
| --- | --- |
| UC-01 (ImportImageAsset) | (i) 画像 decode して PixelSize 取得 (R-01) (ii) SHA-256 計算 (iii) hash 既存確認 (iv) 既存なら ImageAssetImportedAsDuplicate イベント発行して既存返却 (v) 新規なら ImageAsset 生成 + ImageAssetImported 発行 |
| UC-02 (DeleteImageAsset) | (i) `ImageAsset` 存在確認 (NotFound) (ii) **依存 `ImageCopy` の一覧取得** (iii) 1 件以上あれば `DependentCopiesExist` で拒否 (iv) 0 件なら削除 + `ImageAssetDeleted` 発行 |
| UC-05 (CreateImageCopy) | (i) AssetExists 確認 (ii) 初期値の妥当性検証 (iii) `ImageCopy` 生成 (iv) `ImageCopyCreated` 発行 |

### 2.2 validation_decision

UseCase 入口で行う検証:

| 検証対象 | 失敗理由 |
| --- | --- |
| `mime_type` がサポート集合か | `UnsupportedMimeType` |
| Alignment / ScalingMode / Rotation の Enum 値 | `Invalid...` |
| OccupySize の正値性 | `InvalidOccupySize` |
| AutoCrop の両値性 | `InvalidAutoCropSettings` |
| ManualCrop の値域 | `InvalidManualCropFractions` |
| CopyName が空文字列でないか | `InvalidCopyName` |

---

## 3. Entity の意味的定義

### 3.1 ImageAsset

| 概念フィールド | 意味 | 不変条件 |
| --- | --- | --- |
| `id` | 識別子 | 生成後不変 |
| `source_type` | 取り込み元種別 (LocalFile / Url / Clipboard 等) | 列挙値、必須 |
| `original_filename` | 元ファイル名 | 任意 (null 許容) |
| `stored_relative_path` | アプリ管理ストレージ内の相対パス | 必須、空文字不可 |
| `size` | ピクセルサイズ | R-01 で取得済みの値 |
| `file_hash` | SHA-256 (16 進小文字) | R-02 のキー |
| `file_size_bytes` | バイトサイズ | ≥ 0 |
| `mime_type` | MIME タイプ | 必須 |
| `created_at` | 生成時刻 | 生成後不変 |

### 3.2 ImageCopy

| 概念フィールド | 意味 | 不変条件 |
| --- | --- | --- |
| `id` | 識別子 | 生成後不変 |
| `asset_id` | 元画像 ID | R-03、生成後不変 |
| `copy_name` | 人間可読名 | R-11 |
| `transform` | 幾何変形 | R-09 |
| `scaling_mode` | スケーリング方式 | R-04 |
| `alignment` | アンカー点 | R-05 |
| `default_occupy_size` | 配置時の既定占有サイズ | R-10 |
| `auto_crop` | 自動トリミング設定 (集約値) | R-06、null 許容 |
| `manual_crop` | 手動トリミング設定 (集約値) | R-07、null 許容 |
| `created_at` | 生成時刻 | 生成後不変 |
| `updated_at` | 最終更新時刻 | 変更操作で更新 |

### 3.3 値オブジェクトのセマンティクス

#### PixelSize, OccupySize

**GRID_COMPOSITION 側の定義を参照** (`../30-design.md` §3.3)。**二重定義しない**。
同じ値オブジェクト型を共有する責任は、両 Capability の **共通基盤**
(あるいは AI 任意の "shared/" モジュール) に置かれる。

#### ImageTransform (回転 + 反転)

- `rotation`: `Rotation` Enum (時計回り)。`None`, `CW90`, `CW180`, `CW270`
- `flip_x`: 水平反転
- `flip_y`: 垂直反転
- **既定値**: `Rotation.None`, `flip_x=false`, `flip_y=false` (= "Identity")

#### AutoCropSettings (集約値オブジェクト)

- `target_color_argb`: UInt32 (ARGB)。α が 0 なら α 単独判定
- `threshold`: UInt8 (0-255、Chebyshev 距離)
- **集約**: 両値で意味あり (R-06)。`ImageCopy.auto_crop` は `AutoCropSettings | null`

#### ManualCropFraction (集約値オブジェクト)

- `x`, `y`, `width`, `height`: double, [0.0, 1.0]
- 適用順序: **元画像座標系で完結** (元画像 → ManualCrop → Transform → Scaling/Alignment)
- 設計の補足: 座標系の独立性により、Renderer / View / UseCase の 3 経路で同じ比率を共有できる

#### Alignment (9 点アンカー)

セル内での画像位置基準。画像 ≤ セル 軸では「セル内のどこに配置するか」、
画像 > セル 軸では「ソースのどの部分を見せるか」を **同じ値で表現** する
(CSS background-position の単一アンカー設計)。

---

## 4. Event Catalog

> 各イベントの payload と発行タイミングは 21-yaml §events を参照。
> 規約は GRID_COMPOSITION と同じ:
> - 状態変更が成功した時のみ発行
> - 発行と状態変更がテスト可能な形で分離
> - 配信機構は AI 任意

`ImageCopyDeleted` イベントは **特に重要**。GRID_COMPOSITION が購読し、
関連 Placement の扱いを決定するためのトリガーになる (詳細は §5)。

---

## 5. Persistence Boundary と Cascade Decision

### 5.1 Repository インターフェース (規範)

```text
ImageAssetRepository:
  - GetById(asset_id) -> ImageAsset | None
  - Save(asset) -> void
  - Delete(asset_id) -> void
  - ListAll() -> [ImageAsset]
  - FindByHash(hash) -> ImageAsset | None    # R-02 の判定に必須

ImageCopyRepository:
  - GetById(copy_id) -> ImageCopy | None
  - GetByAssetId(asset_id) -> [ImageCopy]    # UC-02 の依存確認に必須
  - Save(copy) -> void
  - Delete(copy_id) -> void
  - ListAll() -> [ImageCopy]
  - Exists(copy_id) -> bool                  # UC-16

ImageBlobStorage:
  - Store(bytes, hash) -> relative_path
  - Load(relative_path) -> bytes
  - Delete(relative_path) -> void
```

### 5.2 Cascade Decision の所在 (重要)

`ImageAsset` 削除時に依存 `ImageCopy` の扱いは:

1. **本 Capability の判断**: 依存があれば `DependentCopiesExist` で **拒否のみ** する
2. **上位 Coordinator の判断**: 拒否を受けて、(a) ユーザーに削除確認を出す、(b) 強制カスケード削除を行う、(c) 操作中止のいずれかを選ぶ
3. **GRID_COMPOSITION の判断**: `ImageCopyDeleted` イベントを購読し、関連 Placement を削除するか拒否するか自分で決める

> [!IMPORTANT]
> この **三層の責任分離** は本サンプルの中核設計。Capability の純度を保ちつつ、
> 現実的なカスケード処理を実現する。AI 実装時は **「本 Capability が cascade 判断を持たない」** 制約を必ず守ること。

### 5.3 トランザクション境界

- UC-01 (`ImportImageAsset`) は **アトミック** (Storage 書込 + Repository 書込)
- UC-02 (`DeleteImageAsset`) はアトミック (依存確認 + 削除を直列化)
- UC-05 〜 UC-15 は単一 entity 更新で十分

---

## 6. テスト戦略 (規範)

> v0.2 学習を継承: random walk / property-based test を **必須**。Anchor tests を同梱。

### 6.1 必須テストカテゴリ

| カテゴリ | 対象 |
| --- | --- |
| Rule unit | 各 Rule R-01〜R-07, R-09〜R-11 (R-08 は「共存可能」のみ確認) |
| UseCase happy path | 各 UC の正常系 |
| UseCase failure path | 各失敗理由 (NotFound 系含む) |
| Event emission | 状態変更と独立に検証 |
| Hash dedup | UC-01 の R-02 (重複取込で 1 物理 Asset) |
| Cascade refusal | UC-02 で依存ある時の DependentCopiesExist |
| Anchor tests AT-01 〜 AT-10 | 必須 |
| **Property-based (1000-step random walk)** | **必須 (v0.2 規範を踏襲)** |

### 6.2 1000-step random walk の対象 invariant

- R-02: 任意の操作列後に hash と Asset が 1:1
- R-03: 任意の操作列後に全 ImageCopy が有効な Asset を参照
- R-06, R-07: AutoCrop / ManualCrop の集約整合性
- "No orphaned blob": `ImageAssetDeleted` 後に Storage 内の blob が残らない (実装は AI 任意)

---

## 7. Worked Examples

### W-1: hash 重複取込 (R-02 の核心)

**Given**: 既に hash `0xABCD...` の `ImageAsset` (A1) が 1 つ存在
**When**: 同じバイト列を `ImportImageAsset` で取り込む
**Then**:
- A1 がそのまま返る (新規 Asset は生成されない)
- `ImageAssetImportedAsDuplicate(existing_asset_id=A1.id)` イベントが発行される
- `ImageAssetImported` は発行されない

### W-2: AutoCrop と ManualCrop の共存 (R-08 関連)

**Given**: `ImageCopy` C1 (AutoCrop=null, ManualCrop=null)
**When**:
1. `ChangeAutoCropSettings(C1, target_color=0xFFFFFFFF, threshold=8)`
2. `ChangeManualCropSettings(C1, x=0.1, y=0.1, width=0.5, height=0.5)`

**Then**:
- 両方の設定が C1 に保存されている (= 共存している)
- どちらが優先されるかの **テストは本 Capability では行わない** (R-08 は RENDERING_EXPORT 側)
- 後で `ChangeManualCropSettings(C1, x=null, y=null, w=null, h=null)` で OFF にすれば、AutoCrop だけが残る

### W-3: 依存 ImageCopy がある時の Asset 削除拒否

**Given**: `ImageAsset` A1 から `ImageCopy` C1, C2 を生成
**When**: `DeleteImageAsset(A1)`
**Then**:
- `DependentCopiesExist(asset_id=A1.id, dependent_copy_ids=[C1.id, C2.id])` を返す
- A1 は削除されない
- 状態変更なし、`ImageAssetDeleted` は発行されない

### W-4: AutoCropSettings の片方だけ null は拒否

**When**: `ChangeAutoCropSettings(C1, target_color=0xFFFFFFFF, threshold=null)`
**Then**:
- `InvalidAutoCropSettings(detail="threshold required when target_color is set")` を返す
- C1 の auto_crop は変更されない (= 直前の値のまま)

### W-5: ImageCopy の自動生成名

**Given**: `ImageAsset` A1 (original_filename="photo.png")
**When**: `CreateImageCopy(asset_id=A1.id, copy_name=null)`
**Then**:
- 新規 `ImageCopy` が生成される
- `copy_name` は null **のまま** (UI 表示時に自動生成する)
- **本 Capability は自動生成名を計算しない** (= projection_decision、UI 層の責任)

> 自動生成名の計算 (例: "photo - copy 1") は **UI 層の projects Role**。本 Capability の責任外。

### W-6: hash 計算の決定性

**Given**: 同じバイト列 X
**When**: `ImportImageAsset(X)` を 2 回 (異なるセッション含む)
**Then**:
- file_hash が両回で完全に一致する
- 2 回目は重複扱い

---

## 8. Anchor Tests

| ID | 対応 | 期待振る舞い |
| --- | --- | --- |
| AT-01 | W-1 | hash 重複取込で既存 Asset が返り、`ImageAssetImportedAsDuplicate` が 1 件発行 |
| AT-02 | W-3 | 依存派生物ありの Asset 削除で `DependentCopiesExist` |
| AT-03 | W-4 | AutoCropSettings の片方 null で `InvalidAutoCropSettings` |
| AT-04 | W-2 | AutoCrop と ManualCrop の共存が許容される (本 Capability では優先関係を適用しない) |
| AT-05 | UC-13 | ManualCrop の `x + width > 1.0` で `InvalidManualCropFractions` |
| AT-06 | UC-15 | `RenameImageCopy(C1, null)` が成功する (null = 自動生成名へ戻す) |
| AT-07 | UC-15 | `RenameImageCopy(C1, "")` が `InvalidCopyName` (空文字は不可) |
| AT-08 | UC-05 | `CreateImageCopy(asset_id=<不在>)` で `NotFound(entity_kind="ImageAsset")` |
| AT-09 | UC-16 | 削除直後の copy_id について `ImageCopyExists` が false を返す |
| AT-10 | 反例 | 1000-step random walk で R-02 / R-03 / R-06 / R-07 が常に成立 |

---

## 9. 実装に関する非規定事項

GRID_COMPOSITION v0.2 と同じ:

- **AI 自由**: 言語、フレームワーク、クラス分割、命名、DI、永続化形式 等
- **AI 不変更**: Rule ID / 名称、UseCase ID / 失敗理由名、Event 名 / 発行タイミング、用語、Anchor test 期待値

---

## 10. 関連ドキュメント

- `10-requirements.md`, `20-capability-bom.md`, `21-image-variant-management.yaml`
- `40-ai-implementation-prompt.md` — AI 実装プロンプト
- `../README.md`, `../90-feasibility-notes.md` (Addendum C で境界調整負荷を観測)
