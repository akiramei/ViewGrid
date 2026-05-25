# 20 — Capability BOM (GRID_COMPOSITION)

> **Version: v0.2** (v0.1 からの主な変更: `NotFound` 失敗理由追加 / UC-09 SetOrder の `order_value` 入力明示)

## このドキュメントの位置づけ

本書は `GRID_COMPOSITION` の **意味構造と意思決定の所在** を定義する。

要求仕様 (`10-requirements.md`) は「何を作るか」を述べた。
本書は **「どこに意思決定があるか」「どの境界を越えてはならないか」** を述べる。

> Capability BOM Audit の中心原則:
> **関与していることは問題ではない。意思決定を所有していることが問題になり得る。**

本書は AI 実装フェーズで **最優先で参照される文書** である。
機械可読版は `21-grid-composition.yaml`。

---

## 1. Capability の同定

| 項目 | 値 |
| --- | --- |
| **ID** | `GRID_COMPOSITION` |
| **Name (日)** | グリッド配置構成 |
| **Name (en)** | Grid Composition |
| **Layer (意味)** | Domain Capability (技術的な「層」ではなく意味境界) |
| **Volatility** | 中 (Rule は安定。UseCase は機能追加で増える可能性あり) |
| **Stakeholder** | 編集者 / 再利用者 / 大判出力者 (要求仕様 §2.1 参照) |

### Purpose (意図文)

> グリッド (N 行 × M 列のセル格子) 上に、画像派生物 (ImageCopy) を、
> 境界と非重複を保証しつつ配置・編成する。
> 配置の妥当性に関する **唯一の権威** である。

「唯一の権威」とは、`PlacementMustFitWithinGrid` と `PlacementsMustNotOverlap` の
**判定者は本 Capability のみ** であり、他 Capability・UI 層・永続化層は判定を持たないことを意味する。

---

## 2. UseCases (本 Capability が提供する操作)

| ID | 名前 | 種別 | 失敗理由 |
| --- | --- | --- | --- |
| UC-01 | `CreateGridCanvas` | command | InvalidDimensions |
| UC-02 | `ChangeGridDimensions` | command | NotFound / InvalidDimensions / WouldOrphanPlacements / WouldConflict |
| UC-03 | `ChangeRowColumnWeights` | command | NotFound / InvalidWeights |
| UC-04 | `ToggleRowColumnLock` | command | NotFound / InvalidIndex |
| UC-05 | `PlaceImageCopy` | command | NotFound / OutOfBounds / Conflict / UnknownCopyId |
| UC-06 | `MovePlacement` | command | NotFound / OutOfBounds / Conflict |
| UC-07 | `SwapPlacements` | command | NotFound / OutOfBounds / Conflict |
| UC-08 | `ResizePlacementOccupancy` | command | NotFound / OutOfBounds / Conflict |
| UC-09 | `ChangePlacementOrder` | command | NotFound / InvalidOrderValue |
| UC-10 | `RemovePlacement` | command | NotFound |
| UC-11 | `ListPlacements` | query | (none — グリッドが存在しないなら空リストを返す) |

> 各 UseCase の事前/事後条件は `10-requirements.md` §3.2 を参照。
> 本書は **失敗理由** の正準名称を固定する (AI は別名を作ってはならない)。

### 2.1 失敗理由 `NotFound` の規範

**v0.2 追加。** UseCase の前提条件 (`GridExists`, `PlacementExists`) が破られた時の正準失敗理由。

- **Payload**: `{ entity_kind: "GridCanvas" | "Placement" | "ImageCopy", entity_id: identity }`
- **UC-05 における `ImageCopy` 不在**: `NotFound(entity_kind="ImageCopy")` ではなく **`UnknownCopyId`** を使う (本 Capability が所有しない foreign reference のため、意味的に別扱い)
- **UC-07 で両配置のうちいずれかが存在しない場合**: `NotFound(entity_kind="Placement", entity_id=...)` を最初に見つかった ID で返す
- **UC-11 (query) はグリッド不在では失敗しない**: 空リストを返す

---

## 3. Rules (本 Capability が保証する意味制約)

| Rule ID | 制約内容 | 種別 | 保証場所宣言 |
| --- | --- | --- | --- |
| R-01 | `PlacementMustFitWithinGrid` | invariant | UseCase 層 (純粋関数で判定) |
| R-02 | `PlacementsMustNotOverlap` | invariant | UseCase 層 (純粋関数で判定) |
| R-03 | `GridDimensionsMustBePositive` | invariant | Domain Model (Entity 構築時) |
| R-04 | `WeightsMustBePositiveIntegers` | invariant | Domain Model (Entity 構築時) |
| R-05 | `WeightArrayLengthMatchesDimension` | invariant | Domain Model + UseCase |
| R-06 | `PlacementOrderMustBeUnique` | invariant | UseCase 層 |
| R-07 | `CellPositionAndOccupySizeAreImmutableInOnePlacement` | lifecycle | Entity の不変性で保証 |
| R-08 | `LockedWeightsAreSkippedInFitAdjustment` | policy | UseCase 層 (Fit 動作) |
| R-09 | `RemovedPlacementOrderMustBeCompacted` | consistency | UseCase 層 (UC-10) |

各 Rule の詳細な保証場所と検証アルゴリズムは `30-design.md` の Rule Ledger に記述。

---

## 4. Entities (本 Capability が所有 / 参照するエンティティ)

### 4.1 所有 (Owned)

| Entity | 説明 | 不変条件 |
| --- | --- | --- |
| `GridCanvas` | グリッドキャンバス | R-03, R-04, R-05 |
| `Placement` | 配置 | R-01, R-02, R-06, R-07 (集合として保証) |

### 4.2 参照のみ (Referenced)

| Entity | 提供 Capability | 参照方法 |
| --- | --- | --- |
| `ImageCopy` | `IMAGE_VARIANT_MANAGEMENT` | `CopyId` (Guid) のみ。本 Capability は実体を持たない |

### 4.3 値オブジェクト

| Value Object | 意味 |
| --- | --- |
| `CellPosition (x, y)` | セル座標。x = 列, y = 行 |
| `OccupySize (width, height)` | 占有セル数。いずれも ≥ 1 |
| `PixelSize (width, height)` | 出力サイズ。いずれも > 0 |

> [!IMPORTANT]
> `ImageCopy` の **特性 (Scaling, Crop, Transform 等) を本 Capability が解釈してはならない**。
> 配置の判定には `CopyId` の存在性のみ使う。

---

## 5. Events (本 Capability が発行するイベント)

| Event | 発生条件 | 主な購読者 (期待値) |
| --- | --- | --- |
| `GridCanvasCreated` | UC-01 成功時 | HISTORY_MANAGEMENT, 永続化 |
| `GridDimensionsChanged` | UC-02 成功時 | HISTORY_MANAGEMENT, 描画再計算 |
| `RowColumnWeightsChanged` | UC-03 成功時 | HISTORY_MANAGEMENT, 描画 |
| `RowColumnLockToggled` | UC-04 成功時 | HISTORY_MANAGEMENT |
| `PlacementCreated` | UC-05 成功時 | HISTORY_MANAGEMENT, 描画 |
| `PlacementMoved` | UC-06 成功時 | HISTORY_MANAGEMENT, 描画 |
| `PlacementsSwapped` | UC-07 成功時 | HISTORY_MANAGEMENT, 描画 |
| `PlacementOccupancyResized` | UC-08 成功時 | HISTORY_MANAGEMENT, 描画 |
| `PlacementOrderChanged` | UC-09 成功時 | HISTORY_MANAGEMENT, 描画 |
| `PlacementRemoved` | UC-10 成功時 | HISTORY_MANAGEMENT, 描画 |

> イベントの **配信機構 (in-process / message bus 等) は規定しない**。
> ただし「副作用」と「状態変更」は分離可能であること。

---

## 6. Decision Ownership 表

> Capability BOM Audit の核心。**どの種類の意思決定がどこに属するか** を固定する。

| Decision 種別 | 所在 (Where) | 例 | 越境した場合のリスク |
| --- | --- | --- | --- |
| `domain_decision` | UseCase + Domain Model | 配置妥当性の判定アルゴリズム / 不変条件 | UI 層に漏れる → 同じ判定が複数箇所で異なる結果 |
| `validation_decision` | UseCase 層 | 入力値の境界・型の妥当性 | Domain 層に混入 → ドメイン意味の希薄化 |
| `workflow_decision` | UseCase + Coordinator | 操作の順序 (UC-07 は内部で UC-06 を 2 回呼ぶ等) | Entity に流出 → Entity が手続き的になる |
| `ui_interaction_decision` | 上位層 (本 Capability 外) | クリックを UC-05 に変換 / 確認ダイアログ | 本 Capability に混入 → UI 依存 |
| `persistence_decision` | 上位層 (Repository 層) | 保存単位・トランザクション境界 | UseCase に混入 → トランザクション無しでテストできない |
| `rendering_decision` | 別 Capability (`RENDERING_EXPORT`) | 配置の見え方・描画順 | 本 Capability に混入 → グリッドが描画形式に依存 |
| `history_decision` | 別 Capability (`HISTORY_MANAGEMENT`) | Undo の粒度・統合可能性 | UseCase に混入 → ドメインが履歴に依存 |

### 6.1 重要な禁則

- **UI 層は妥当性の判定者になってはならない**: UI は「拒否されたら表示する」だけ。判定そのものは UseCase
- **永続化層は重複検出をしてはならない**: DB ユニーク制約に依存して整合性を担保するのは禁止 (Rule の権威が逃げる)
- **ImageCopy の意味解釈は不可**: 本 Capability は `CopyId` の存在性しか見ない

---

## 7. Role Taxonomy (期待される Role と禁則)

> AI が実装したコードが本 Capability に対してどの Role を持ちうるかの規定。
> 実装スタイルが AI 任意でも、**Role の組み合わせは固定**。

### 7.1 Allowed Role (許容される関与)

| Role | 説明 | 例 |
| --- | --- | --- |
| `observes` | 状態変更を購読する | UI が `PlacementMoved` を購読して再描画 |
| `projects` | 状態を表示用に整形する | グリッド表示用 ViewState への変換 |
| `invokes` | UseCase を呼び出す | UI が `MovePlacement` を呼ぶ |
| `coordinates` | 複数 UseCase / Capability を調停する | 入れ替え操作の調停 |
| `enforces` | 本 Capability の Rule を保証する | UseCase 層 |
| `owns` | 本 Capability の状態と Decision を所有する | UseCase + Domain Model |
| `persists` | 状態を永続化する | Repository (本 Capability の Domain Model を保存) |

### 7.2 Suspicious / Forbidden

| 状況 | 判定 | 理由 |
| --- | --- | --- |
| UI 層が `owns` を持つ | **Suspicious** | Decision が UI に漏れている疑い |
| UI 層が `enforces` を持つ | **Forbidden** | Rule の保証権限が UI に流出 |
| Repository が `enforces` を持つ | **Forbidden** | 永続化が Rule の権威を持つと、書込前後の整合が分裂する |
| ImageCopy を「拡張」する | **Forbidden** | 本 Capability は ImageCopy を解釈しない |
| `RENDERING_EXPORT` のために描画情報を保持する | **Forbidden** | rendering_decision の越境 |

---

## 8. Capability Boundaries (周辺 Capability との境界)

```text
              ┌─────────────────────────┐
              │ IMAGE_VARIANT_MANAGEMENT│
              │  (ImageCopy 作成・編集) │
              └───────────┬─────────────┘
                          │ provides ImageCopy (CopyId)
                          ▼
        ┌─────────────────────────────────────┐
        │       GRID_COMPOSITION              │
        │   (本 Capability)                   │
        │  - 配置の妥当性                     │
        │  - グリッド寸法・重み・ロック        │
        │  - 配置順序                         │
        └──────────┬──────────────┬───────────┘
                   │ emits events │ owns entities
                   ▼              ▼
          ┌────────────────┐  ┌────────────────┐
          │ HISTORY_MANAGE │  │ RENDERING_     │
          │    MENT        │  │    EXPORT      │
          │ (Undo/Redo)    │  │ (PNG 生成)     │
          └────────────────┘  └────────────────┘
```

### 8.1 IMAGE_VARIANT_MANAGEMENT との境界 (v0.2 で正式接続)

隣接 Capability の正式サンプル: `image-variant-management/` 配下を参照。

- 本 Capability は **CopyId のみ** を保持する
- ImageCopy の存在性確認は **IMAGE_VARIANT_MANAGEMENT の UC-16 `ImageCopyExists`** を呼ぶ
  - UC-05 (`PlaceImageCopy`) の preconditions `ImageCopyExists` が破れた時 `UnknownCopyId` で返す
- ImageCopy のトリミング・スケーリング設定変更は **本 Capability の知るところではない**
  (IMAGE_VARIANT_MANAGEMENT が権威)
- ImageCopy が削除されたとき (`ImageCopyDeleted` イベント発行):
  - **本 Capability が購読し**、関連 Placement の扱い (削除 / 保留 / エラー) を **自分で決める**
  - IMAGE_VARIANT_MANAGEMENT は cascade 判断を持たない (純度を保つため `DependentCopiesExist` で拒否のみ)
  - 詳細な cascade フローは上位 Coordinator (両 Capability 外) が調停

> [!IMPORTANT]
> v0.1 の本書では「上位 Coordinator が cascade 削除を発行」とだけ書いていたが、
> v0.2 で IMAGE_VARIANT_MANAGEMENT 側の正式サンプルが書かれたことにより、
> 上記の **三層責任分離** (本 Capability / IMAGE_VARIANT / Coordinator) が明確になった。
> これは隣接 Capability を書き始めて初めて見えた境界明確化の例。

### 8.2 HISTORY_MANAGEMENT との境界

- 本 Capability の UseCase は **Event を発行する**
- 本 Capability は Undo / Redo の **粒度・統合 (coalesce) を決定しない**
- 「変更前の観測可能な状態」を復元できる形でイベントを表現する責任のみ持つ

### 8.3 RENDERING_EXPORT との境界

- 本 Capability は **配置の論理情報** (位置・占有・順序) のみ提供
- ピクセル座標への変換・PhotoBoard 装飾・PNG 出力は本 Capability の **範囲外**
- `CanvasSize` (PixelSize) は本 Capability が **保持** するが **解釈しない** (RENDERING が解釈する)

---

## 9. 観測可能性 (Audit のための要件)

AI が実装したコードに対し、事後監査を可能にするため、以下を満たすこと。

- 各 UseCase は **入力 → 結果** の対応が単一の関数として表現可能であること
- Rule の保証場所 (R-01〜R-09) が **コード上で 1 箇所** に存在すること
  - 複数箇所に分散している場合は `suspected_overreach` として記録される
- イベント発行は **状態変更と独立** に観測可能であること (テストで検証可能)

---

## 10. 関連ドキュメント

- `21-grid-composition.yaml` — 本書の機械可読版 (AI が参照する正準)
- `30-design.md` — Rule ledger, Entity 意味的定義, Event catalog
- `40-ai-implementation-prompt.md` — AI への指示テンプレート
