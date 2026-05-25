# 30 — 設計書 (GRID_COMPOSITION)

> **Version: v0.2** (v0.1 からの主な変更: UC-07 post-swap intersection check 明文化 / Worked example §7 追加 / random walk + property test を必須テストへ格上げ / anchor tests §8 新設)

## このドキュメントの位置づけ

本書は Capability `GRID_COMPOSITION` の **設計の意味的詳細** を述べる。

- **要求仕様** (`10-requirements.md`): 何を作るか
- **Capability BOM** (`20-capability-bom.md` / `21-grid-composition.yaml`): どこに意思決定があるか
- **設計書 (本書)**: 各 Rule / Entity / Event / Persistence の **保証内容** と **意味的契約**

> [!IMPORTANT]
> 本書は **コード構造 (クラス分割・ファイル配置・パターン適用) を規定しない**。
> 規定するのは「何を保証するか」「何を意味するか」のみ。
> 実装方針は AI に委ねる。

---

## 1. Rule Ledger

> 各 Rule の保証場所宣言と検証アルゴリズム。AI はこの台帳に列挙された Rule を
> **正確に列挙された場所** で保証しなければならない (越境・複製は禁止)。

### R-01: PlacementMustFitWithinGrid

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | UseCase 層 (純粋関数として実装) |
| Applies to | UC-05, UC-06, UC-07, UC-08, UC-02 |
| Verification | 下記アルゴリズム |

**意味**: 配置の占有矩形がグリッド境界内に完全に収まる。

**判定**:

```text
Given:
  position = (x, y)
  occupy_size = (w, h)
  grid_rows = R
  grid_cols = C

Valid iff:
  x >= 0
  y >= 0
  x + w <= C
  y + h <= R
```

**失敗時の応答**: UseCase は `OutOfBounds` を返し、状態を変更しない。
イベントは発行しない。

---

### R-02: PlacementsMustNotOverlap

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | UseCase 層 (純粋関数として実装) |
| Applies to | UC-05, UC-06, UC-07, UC-08, UC-02 |
| Verification | 下記アルゴリズム |

**意味**: グリッド上の任意の 2 配置の占有セル集合が交差しない。

**判定**:

```text
Given:
  candidate_cells = OccupiedCells(position, occupy_size)
  existing_placements = (グリッド内の他の全配置、ただし除外対象を引いた集合)

Valid iff:
  For each existing in existing_placements:
    OccupiedCells(existing.position, existing.occupy_size)
      ∩ candidate_cells == ∅

OccupiedCells(origin, size) = {
  (origin.x + dx, origin.y + dy)
  for dx in [0, size.width),
      dy in [0, size.height)
}
```

**除外対象 (「既存配置との衝突」検査からの除外)**:
- UC-06 (移動): 自身の placement_id
- UC-07 (入れ替え): 双方の placement_id
- UC-08 (リサイズ): 自身の placement_id
- UC-02 (寸法変更): なし (既存全配置を検証)

> [!IMPORTANT]
> **UC-07 (Swap) 特有の追加検査** (v0.2 で明文化):
> 上記の「既存配置との衝突」検査だけでは、**A の新位置 (= B の現位置) と
> B の新位置 (= A の現位置)** が **互いに重なるケース** を捕捉できない。
> 例: A が (0,0) 1×1、B が (0,0) 2×1 の場合、A の新占有 {(0,0)} と
> B の新占有 {(0,0), (1,0)} がセル (0,0) で衝突する。
>
> UC-07 の workflow_decision は、**A の新占有セル集合と B の新占有セル集合の
> 交差が空でないことを別途検証** する責任を持つ (§2.2 UC-07 を参照)。
> この追加検査は「R-02 ロジックの 2 箇所目」ではなく、UC-07 の workflow_decision
> として位置づけられる (`suspected_overreach` ではない)。

**失敗時の応答**: UseCase は `Conflict` を返し、状態を変更しない。
イベントは発行しない。**衝突相手の placement_id** を結果 (`conflicting_placement_ids`)
に含めること (1 個以上、UC-07 の A-B 相互衝突なら両 ID)。

---

### R-03: GridDimensionsMustBePositive

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | Domain Model (GridCanvas 構築時) |
| Applies to | UC-01, UC-02 |

**意味**: `grid_rows ≥ 1` かつ `grid_cols ≥ 1`。`canvas_size.width > 0` かつ `canvas_size.height > 0`。

**失敗時**: Entity を生成しない (UseCase レベルでは `InvalidDimensions`)。

---

### R-04: WeightsMustBePositiveIntegers

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | Domain Model |
| Applies to | UC-01, UC-03 |

**意味**: `col_weights` / `row_weights` の全要素が **正の整数**。

> 浮動小数を許容しない理由: 表示計算で誤差が累積するのを避けるため。
> 実際の比率はピクセル分配時に整数比から計算する。

---

### R-05: WeightArrayLengthMatchesDimension

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | Domain Model + UseCase 層 |
| Applies to | UC-01, UC-02, UC-03 |

**意味**: `col_weights.length == grid_cols` かつ `row_weights.length == grid_rows`。

**UC-02 における特例 (寸法変更時の調整)**:

```text
寸法を C_old → C_new に変更する場合:
  if C_new > C_old:
    新規列の重みは 1 (均等) で末尾追加
  if C_new < C_old:
    削除される列の重みは捨てる
    (ロック状態も同様に追加/削除)
```

この調整自体は **workflow_decision** であり、UseCase 層が責任を持つ (Domain Model は再生成のみ)。

---

### R-06: PlacementOrderMustBeUnique

| 項目 | 内容 |
| --- | --- |
| Kind | invariant |
| Owned by | UseCase 層 |
| Applies to | UC-05, UC-09, UC-10 |

**意味**: 同一 GridCanvas 内の Placement の `placement_order` 集合に重複がない。

**初期値方針**:
- UC-05 (新規配置): `max(existing_orders) + 1` (空なら 1)
- UC-09 (並べ替え): 操作後も全 order が一意であること

---

### R-07: CellPositionAndOccupySizeAreImmutableInOnePlacement

| 項目 | 内容 |
| --- | --- |
| Kind | lifecycle |
| Owned by | Entity の不変性で保証 |

**意味**: 個別の `Placement` インスタンスにおいて、`position` と `occupy_size` は
不変として **論理的に観測可能** であること。

> [!NOTE]
> 「論理的に観測可能」とは:
> - 値を変更したい場合、新しい Placement を作って差し替える形でも可
> - in-place で書き換える実装でも、**変更前後を独立に観測できる API** を提供すれば可
>
> AI は実装スタイルを任意に選んでよいが、テストで「変更前のスナップショット」と
> 「変更後のスナップショット」を独立に取得できなければならない (R-08 のテスト容易性のため)。

---

### R-08: LockedWeightsAreSkippedInFitAdjustment

| 項目 | 内容 |
| --- | --- |
| Kind | policy |
| Owned by | UseCase 層 (Fit 動作) |
| Applies to | UC-02 (寸法変更に伴う重み再配分が発生する場合のみ) |

**意味**: ロックされた軸インデックスは Fit 動作の対象外。

**Fit 動作のアルゴリズム (仕様、v0.2 で修正)**:

```text
Fit(weights, locked, target_dimension):
  if target_dimension == weights.length:
    return weights (調整不要)

  if target_dimension > weights.length:
    # 拡張: 末尾に重み 1 を追加 (ロックは false)
    return weights + [1] * (target_dimension - weights.length)

  if target_dimension < weights.length:
    # 縮小: 末尾から削除
    # ただし locked == true の要素は削除対象から除外し、優先度の低い
    # (末尾から) アンロック要素を順に削除する
    # 削除可能なアンロック要素が不足する場合 (= ロック要素を削らないと縮小不能)
    # は WouldOrphanPlacements 系の失敗扱いとし、UC-02 の Conflict / WouldConflict
    # ファミリにマッピングする (専用の失敗理由は作らない)
    ...
```

> [!IMPORTANT]
> **v0.2 修正**: v0.1 で `WouldDestroyLockedAxis` という失敗理由を雛形コメントに残していたが、
> これは `21-grid-composition.yaml §canonical_failure_reasons` には存在しない名前であり、
> FORBIDDEN「失敗理由を追加してはならない」と衝突していた。
> v0.2 では `WouldOrphanPlacements` / `WouldConflict` (UC-02 の既存失敗理由) で表現する。
>
> 具体的には:
> - 「ロック要素を消さないと縮小できない」状況 = 「実質的に既存配置が境界外になる」状況なので
>   `WouldOrphanPlacements` で報告する (payload に該当ロック軸 index を含めてよい)
> - もしくは UC-02 として `WouldConflict` を使う

> Fit 動作のロジックには **複数の妥当な選択** がある (どの順序で削るか等)。
> 上記は雛形であり、最終的なアルゴリズムは AI 実装時に確定させること。
> ただし「ロックは尊重される」「結果は決定的」の 2 つは必須。

---

### R-09: RemovedPlacementOrderMustBeCompacted

| 項目 | 内容 |
| --- | --- |
| Kind | consistency |
| Owned by | UseCase 層 |
| Applies to | UC-10 |

**意味**: 配置削除後、残った placement の `placement_order` は `1..N` の連続値となる。

```text
削除前: orders = [1, 2, 3, 4, 5]、order=3 を削除
削除後: orders = [1, 2, 3, 4]  (旧 4, 5 が 3, 4 に詰める)
```

---

## 2. Decision Specification (詳細)

### 2.1 domain_decision の詳細

本 Capability の `domain_decision` は **配置の妥当性に関する判定** に集約される。
具体的な判定責任は次の通り:

| 判定対象 | 担当 |
| --- | --- |
| 占有矩形がグリッド内か | R-01 |
| 占有矩形同士の交差 | R-02 |
| グリッド寸法の妥当性 | R-03 |
| 重みの妥当性 | R-04, R-05 |
| 配置順序の一意性 | R-06 |

これらは **すべて純粋関数として実装可能** でなければならない (テスト容易性のため)。
DB アクセス・I/O を含む実装は禁止。

### 2.2 workflow_decision の詳細

| 操作 | 内部手順 | 備考 |
| --- | --- | --- |
| UC-07 (Swap) | (i) 双方の配置を取得 (片方でも存在しなければ `NotFound`) (ii) R-01 を双方の新位置で検証 (iii) R-02 を双方の新位置で検証 (除外: 双方) (iv) **A の新占有セル集合と B の新占有セル集合の交差を検証** (v) 双方の位置を同時に更新 (vi) `PlacementsSwapped` 発行 | 部分的成功を許さない (どちらかが失敗したら両方ロールバック)。**手順 (iv) は v0.2 で明文化。これがないと A-B 相互衝突が捕捉されない (§7 worked example 参照)** |
| UC-02 (寸法変更) | (i) 新寸法での既存配置全てを R-01 + R-02 で検証 (ii) 失敗なら拒否 (iii) 重み配列を Fit 動作で調整 (R-05, R-08) (iv) `GridDimensionsChanged` 発行 | 配置の自動移動はしない (失敗時はユーザーに判断を委ねる) |
| UC-10 (削除) | (i) 配置を削除 (ii) 残り順序を R-09 で詰める (iii) `PlacementRemoved` 発行 | 詰め直しは状態変更だが、`PlacementOrderChanged` は別途発行しない (`PlacementRemoved` に内包) |

### 2.3 validation_decision の詳細

入力検証は **UseCase の入口** で行い、Domain Model には妥当な値しか渡さない。
ただし、検証ロジックそのものは Domain Model の不変条件と共通化してよい (重複保証は許容)。

| 検証対象 | 場所 | 失敗時 |
| --- | --- | --- |
| 入力の型・null | UseCase | InvalidArgument |
| 入力の範囲 (例: 寸法 > 0) | UseCase | InvalidDimensions / InvalidWeights / InvalidIndex |
| 入力の参照妥当性 (ImageCopy 存在) | UseCase + Repository | UnknownCopyId |

---

## 3. Entity の意味的定義

> データ構造の物理表現ではなく、**意味** を定義する。実装言語の型は AI 任意。

### 3.1 GridCanvas

| 概念フィールド | 意味 | 必須 | 不変条件 |
| --- | --- | --- | --- |
| `id` | このグリッドを一意に識別する不透明な値 | はい | 生成後不変 |
| `name` | 人間可読の名前 | はい | 空文字を許容するかは AI 任意 (推奨: 許容、トリミングは UI 側) |
| `grid_rows` | 行数 | はい | R-03 |
| `grid_cols` | 列数 | はい | R-03 |
| `col_weights` | 列の比率 | はい | R-04, R-05 |
| `row_weights` | 行の比率 | はい | R-04, R-05 |
| `col_locked` | 列のロック状態 | はい | 長さ = grid_cols、既定 false |
| `row_locked` | 行のロック状態 | はい | 長さ = grid_rows、既定 false |
| `canvas_size` | 最終出力サイズ (px) | はい | R-03 |
| `created_at` | 生成時刻 | はい | 生成後不変 |
| `updated_at` | 最終更新時刻 | はい | 変更操作で更新 |

> [!NOTE]
> **「グリッドが空 (placement が 0 個)」は正常状態**。新規作成直後はこの状態。

### 3.2 Placement

| 概念フィールド | 意味 | 必須 | 不変条件 |
| --- | --- | --- | --- |
| `id` | 配置の一意な識別子 | はい | 生成後不変 |
| `grid_id` | 所属する GridCanvas の ID | はい | 生成後不変 |
| `copy_id` | 参照する ImageCopy の ID | はい | 生成後不変 |
| `position` | 占有左上のセル座標 | はい | R-01, R-02 |
| `occupy_size` | 占有セル数 | はい | R-01, R-02 |
| `placement_order` | z-order | はい | R-06 |
| `created_at` | 生成時刻 | はい | 生成後不変 |

### 3.3 値オブジェクトのセマンティクス

#### CellPosition

- `x` は列方向、`y` は行方向 (CSS と異なる、グラフィクス慣習)
- 範囲は `0 ≤ x < grid_cols`, `0 ≤ y < grid_rows`
- 比較は値の等価性 (`==`) で定義

#### OccupySize

- `width` は列方向、`height` は行方向
- 両者とも `≥ 1`
- 矩形のみ (任意形状の占有は規定外)

#### PixelSize

- 単位はピクセル
- `width > 0`, `height > 0`
- 本 Capability は **保持するだけ**。解釈は `RENDERING_EXPORT`

---

## 4. Event Catalog (詳細)

> 各イベントの payload 形式と発行タイミング。

> [!IMPORTANT]
> イベントは **状態変更が成功した時にのみ** 発行する。失敗時は発行しない。
> 発行と状態変更の **テスト可能な分離** を維持すること (Capability BOM Audit Phase 3 で検証)。

### 4.1 状態変更イベント (発行は UseCase 成功時)

| Event | Payload | 用途 |
| --- | --- | --- |
| `GridCanvasCreated` | `grid_id`, `snapshot (作成直後の GridCanvas 全体)` | HISTORY (Undo = 削除)、永続化 |
| `GridDimensionsChanged` | `grid_id`, `before { rows, cols, col_weights, row_weights, col_locked, row_locked }`, `after { ... }` | HISTORY、描画再計算 |
| `RowColumnWeightsChanged` | `grid_id`, `axis`, `before_weights`, `after_weights` | HISTORY、描画 |
| `RowColumnLockToggled` | `grid_id`, `axis`, `index`, `after_state` | HISTORY |
| `PlacementCreated` | `placement_id`, `snapshot (作成直後の Placement 全体)` | HISTORY (Undo = 削除)、描画 |
| `PlacementMoved` | `placement_id`, `before_position`, `after_position` | HISTORY、描画 |
| `PlacementsSwapped` | `placement_id_a`, `placement_id_b`, `before_a (position)`, `before_b (position)` | HISTORY、描画 |
| `PlacementOccupancyResized` | `placement_id`, `before_size`, `after_size` | HISTORY、描画 |
| `PlacementOrderChanged` | `grid_id`, `before_order_map { placement_id → order }`, `after_order_map { ... }` | HISTORY、描画 |
| `PlacementRemoved` | `placement_id`, `snapshot_before (削除直前の Placement)`, `compacted_order_map (残った placement の新 order)` | HISTORY (Undo = 復元)、描画 |

### 4.2 イベント発行に関する規約

- **発行機構は規定しない**: in-process pub/sub, message bus, EventStore など AI 任意
- **配信保証も規定しない**: at-most-once / at-least-once いずれでも可。ただしテスト容易性のため "ローカル同期発行" を推奨
- **同期性**: UseCase の戻り値が成功であること = イベントが発行されたこと、と等価であるべき (順序保証は規定しない)

---

## 5. Persistence Boundary

> 永続化は **本 Capability の関心事ではない**。
> しかし境界の意味を AI 実装時に逸脱させないため、以下を規定する。

### 5.1 何を Repository に渡すか

本 Capability は次の Repository インターフェースを「使う」ことを許容する:

```text
GridCanvasRepository:
  - GetById(grid_id) -> GridCanvas | None
  - Save(grid_canvas) -> void
  - Delete(grid_id) -> void
  - ListAll() -> [GridCanvas]    # ワークスペース範囲

PlacementRepository:
  - GetById(placement_id) -> Placement | None
  - GetByGrid(grid_id) -> [Placement]
  - Save(placement) -> void
  - Delete(placement_id) -> void

ImageCopyExistenceCheck:
  - Exists(copy_id) -> bool
  # v0.2: IMAGE_VARIANT_MANAGEMENT の UC-16 `ImageCopyExists` を呼ぶ薄いアダプタ。
  # ImageCopy の本体取得は IMAGE_VARIANT_MANAGEMENT のもの。
  # 詳細: image-variant-management/20-capability-bom.md §8.1
```

これら Repository の実装は **本 Capability の責任外**。
ただし **Rule の保証を Repository に依存させない** こと:

- DB のユニーク制約に R-06 (placement_order の一意性) の保証を委ねる → **Forbidden**
- DB のチェック制約に R-01, R-02 を委ねる → **Forbidden**
- 「存在しないなら作る」のような race を許容する設計 → **Suspicious**

### 5.2 トランザクション境界

- UC-07 (Swap) は **アトミック** であること (片方だけ更新されない)
- UC-02 (寸法変更) も **アトミック**
- 他の UC は単一 entity 単位の更新で十分

トランザクションの **実装機構は任意** (DB トランザクション / メモリスナップショット / イベントソーシング等)。

---

## 6. テスト戦略 (意味契約として)

> AI はテストの **存在** と **粒度** を満たす責任を負う。
> テストフレームワーク・ファイル配置は任意。

### 6.1 必須テストカテゴリ

| カテゴリ | 対象 | 例 |
| --- | --- | --- |
| **Rule unit test** | 各 Rule (R-01〜R-09) | R-01: 境界ぴったり、はみ出し 1 セル、負の座標 |
| **UseCase happy path** | 各 UC の正常系 | UC-05 で空グリッドに 1 配置 |
| **UseCase failure path** | 各失敗理由 | UC-05 で OutOfBounds, Conflict, UnknownCopyId |
| **Event emission** | 状態変更と独立にイベント発行を検証 | UC-05 成功時に PlacementCreated が 1 件 |
| **Invariant after operation** | 全 UC 完了後に R-01〜R-09 が成立 | プロパティベース可 |

### 6.2 推奨される追加テスト

- **境界条件**: グリッドの 4 隅 / OccupySize 1×1 と N×M / placement 数 0 と max
- **R-07 検証**: 同一 Placement の position を 2 回観測すると、変更操作の前後で異なる値が得られる
- **R-09 検証**: 連続 remove での order 詰め直し

### 6.3 反例ベース / Property-based test (**v0.2 で必須化**)

> [!IMPORTANT]
> v0.1 で「推奨」だった項目を v0.2 で **必須テストカテゴリへ格上げ**。
> Phase 2 実 AI 試行で **このカテゴリだけが Swap 自身排除曖昧さの実バグを検出した** ことが根拠。

- ある UseCase の入力空間を一般化し、ランダムに **最低 1000 ステップ** の操作列を実行して
  invariant が崩れないことを確認
- 例: 任意のグリッドサイズ・任意の配置・任意の UC 呼び出し列で `PlacementsMustNotOverlap` が常に成立
- 検出すべき代表的バグ: UC-07 の A-B 相互衝突 (§7 worked example W-3 を参照)
- 推奨スコープ: 全 UC のランダム組み合わせ + すべての invariant (R-01 〜 R-06, R-09)

実装ライブラリは AI 任意 (Python `hypothesis`、JS `fast-check`、独自実装等)。
ただし **seed 固定で再現可能** であること。

---

## 7. Worked Examples (**v0.2 新設**)

> サンプル文書だけでは AI の解釈が分かれた事例について、**正準の振る舞いを worked example で固定** する。
> AI は実装時にこれらの例を **テストケースとして必ず含める** こと。

### W-1: UC-05 で空グリッドに最初の配置

**Given**: 3×3 のグリッド (placement 0 件)
**When**: `PlaceImageCopy(grid_id, copy_id=C1, position=(0,0), occupy_size=(1,1))`
**Then**:
- 配置 P1 が生成される
- `P1.placement_order == 1`
- `PlacementCreated` イベントが 1 件発行される

### W-2: UC-06 (Move) で自身を除外した衝突検査

**Given**:
- 3×3 のグリッド
- 配置 A: position=(0,0), occupy_size=(2,1), order=1
- 配置 B: position=(2,0), occupy_size=(1,1), order=2

**When**: `MovePlacement(A, new_position=(0,0))` (同じ位置への移動 = no-op)
**Then**:
- **失敗してはならない** (自身は衝突対象から除外されるため)
- 状態変更は実質ゼロでも `PlacementMoved` を発行するかは **AI 任意** (推奨: 発行しない最適化を認める。テストは「失敗しないこと」のみを検査)

### W-3: UC-07 (Swap) で A/B 非対称サイズの相互衝突 (**重要**)

**Given**:
- 3×3 のグリッド
- 配置 A: position=(0,0), occupy_size=(1,1), order=1
- 配置 B: position=(0,0)... ではなく **(1,0), occupy_size=(2,1)** とする
  - つまり A は 1×1、B は 2×1 (横長)

**When**: `SwapPlacements(A, B)`
**Then**:
- A の新位置 = B の元位置 = (1,0)、A の新占有 = (1,1) → セル集合 = `{(1,0)}`
- B の新位置 = A の元位置 = (0,0)、B の新占有 = (2,1) → セル集合 = `{(0,0), (1,0)}`
- **両者の新占有セル集合の交差** = `{(1,0)}` ≠ ∅
- **Conflict を返す**: `conflicting_placement_ids = [A.id, B.id]`
- 状態変更なし、イベント発行なし

> このケースは **R-02 の「除外: 双方」だけでは捕捉できない**。
> UC-07 workflow_decision の手順 (iv) で明示的に検証すること。
> Phase 2 実 AI 試行ではこのケースを取り逃がし、1000-step random walk で
> 実バグとして検出された (v0.1 → v0.2 改訂のトリガとなった事例)。

### W-4: UC-09 (ChangePlacementOrder) の SetOrder で値を渡す

**Given**:
- 3 つの配置 P1 (order=1), P2 (order=2), P3 (order=3)

**When**: `ChangePlacementOrder(P3, operation=SetOrder, order_value=1)`
**Then**:
- P3 が order=1 に移動
- P1, P2 は 2, 3 に押し下げ (1..N の連続性は維持される)
- 最終: P3=1, P1=2, P2=3
- `PlacementOrderChanged` 発行 (payload に before/after の order map)

### W-5: UC-10 (Remove) の order compaction

**Given**: 4 つの配置 P1=1, P2=2, P3=3, P4=4

**When**: `RemovePlacement(P2)`
**Then**:
- P2 が削除
- 残り順序: P1=1, P3=2, P4=3 (1..N に詰める)
- `PlacementRemoved` 発行 (payload の `compacted_order_map` に詰め直し結果)

### W-6: NotFound の振る舞い (v0.2 で明文化)

**Given**: 空のリポジトリ
**When**: `MovePlacement(placement_id=<存在しない GUID>, new_position=(0,0))`
**Then**:
- `NotFound(entity_kind="Placement", entity_id=<その GUID>)` を返す
- 状態変更なし、イベント発行なし

---

## 8. Anchor Tests (**v0.2 新設**)

> サンプル文書だけでは解釈曖昧さが残るため、**5〜10 件の reference test** を本 Capability に同梱する。
> AI は実装時にこれらをそのまま (テスト関数として) ポートし、**すべてパスすること**。
> Anchor tests は方法論側の概念であり、本サンプルは GRID_COMPOSITION での具体例を提供する。

### Anchor Test 一覧

| ID | 対応 | 期待振る舞い |
| --- | --- | --- |
| AT-01 | W-1 | 空グリッドへの初配置で order=1 |
| AT-02 | W-2 | 同位置 Move で自身衝突なし |
| AT-03 | W-3 | **A-B 非対称サイズ swap で Conflict** |
| AT-04 | W-4 | SetOrder で他配置が押し下げ |
| AT-05 | W-5 | Remove 後の order 詰め直し |
| AT-06 | W-6 | NotFound payload に entity_kind を含む |
| AT-07 | 反例 | 1000-step random walk で R-01, R-02, R-06 が常に成立 |
| AT-08 | 反例 | 任意の操作列後に `UC-11 ListPlacements` の結果が z-order 昇順 |
| AT-09 | 境界 | UC-02 で寸法縮小時、境界外になる配置があれば WouldOrphanPlacements |
| AT-10 | 境界 | UC-04 ToggleRowColumnLock で index が範囲外なら InvalidIndex |

### Anchor Tests の運用規範

- AI は AT-01 〜 AT-10 を **テスト関数名から検索可能** な形で実装すること (例: `test_at_01_*`)
- すべてパスしない実装は **未完成** とみなす (PostImplementation Self-Audit 必須項目)
- AI が「テストの仕様が曖昧」と判断した場合は、テストを変更せず実装ノートに `unclear` として記録

---

## 9. 実装に関する非規定事項 (AI の自由度)

以下は **AI が自由に決めてよい**:

- プログラミング言語
- フレームワーク (UI / ORM / テストランナー)
- クラス・モジュール分割
- ファイル配置
- 命名規約 (本書の用語集に従う限り)
- DI コンテナの使用有無
- 関数型 vs オブジェクト指向
- イミュータブル vs ミュータブル実装 (R-07 の制約は満たす)
- イベント発行機構
- ロギング・テレメトリ

以下は **AI が変更してはいけない**:

- Rule の ID と名称
- UseCase の ID と失敗理由名
- Event の名前と発行タイミング
- Capability 境界 (本 Capability が解釈してはならないものの範囲)
- Decision ownership 表
- 用語集の語の意味

---

## 10. 関連ドキュメント

- `10-requirements.md` — 要求仕様
- `20-capability-bom.md` — 意味境界と Decision ownership
- `21-grid-composition.yaml` — 機械可読版
- `40-ai-implementation-prompt.md` — AI 実装プロンプト雛形
