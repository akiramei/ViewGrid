# 00 — Codebase Convention Contract (横断規約契約) — GRID_COMPOSITION × IMAGE_VARIANT_MANAGEMENT

> **Status: ステップ 2 本体用の具体契約インスタンス** (方法論本体側の規範は `../methodology-extensions/21-codebase-convention-contract.md` を参照)
> **Scope**: 本契約は `GRID_COMPOSITION` と `IMAGE_VARIANT_MANAGEMENT` を **1 つのコードベースに同時生成** する際の横断規約。
> **由来**: Addendum E (候補 E ステップ 1) で観測された 6 カテゴリの規約衝突を **事前に消す** ための契約。

## この契約の目的

Addendum E は、独立生成された 2 実装が **6 カテゴリの規約衝突** により compose 不可だったことを実コードで示した。
本契約は、その 6 カテゴリすべてを **生成前に物理表現レベルで固定** し、
**アダプタ 0 行で 2 Capability が結線できる** ことを目指す。

> [!IMPORTANT]
> 本契約は **物理表現のみ** を規定する。Rule / Decision / Capability 境界の **意味** は
> 各 Capability の 20/21/30 が引き続き権威。本契約はそれらと矛盾してはならない。

---

## 1. 契約項目 (全 Capability が逐一従う)

### C-IDENTITY — identity の物理型

- すべての identity (`GridId`, `PlacementId`, `CopyId`, `AssetId` 等) は **`uuid.UUID` オブジェクト** で表現する。
- **`str` への変換・保持を禁止** (境界での UUID↔str 変換を発生させないため)。
- 生成は `uuid.uuid4()` を直接使う (`str(uuid.uuid4())` は禁止)。

> Addendum E の衝突 #4 (UUID vs str) を消す。

### C-SHARED-PLACEMENT — 共有値オブジェクトの物理配置

- `OccupySize` / `PixelSize` は **`src/shared/value_objects.py` に 1 定義のみ**。
- 両 Capability は **この 1 定義を import** する。**局所複製・再定義を禁止**。

> Addendum E の衝突 #2 (別モジュール定義 = 別型) を消す。

### C-VALUE-SEMANTICS — 共有値オブジェクトのコンストラクタ契約

- `OccupySize` / `PixelSize` は `@dataclass(frozen=True)`。
- **`bool` を `int` として拒否する** (`OccupySize(True, 1)` は `TypeError`)。
  - GRID v0.2 の厳しい側に統一 (Addendum E の衝突 #3 を消す)。
- 値は正の整数 (`>= 1`)。違反は `TypeError` / `ValueError`。

### C-RESULT — Result / 失敗ラッパの命名と配置

- 成功 = **`Ok`**、失敗 = **`Err`**。**`src/shared/result.py` に 1 定義のみ**。
- **`Failure` 等の同義語を作らない** (Addendum E の衝突 #5 を消す)。
- `Result[T]` は両 Capability がこの 1 定義を import する。

### C-LAYOUT — モジュールレイアウト

- **`src/` layout** に統一 (Addendum E の衝突 #1 を消す)。
- パッケージ構成:
  ```text
  src/
  ├── shared/
  │   ├── value_objects.py   # OccupySize, PixelSize (C-SHARED-PLACEMENT)
  │   ├── result.py          # Ok, Err (C-RESULT)
  │   └── ports.py           # 境界 Protocol (C-BOUNDARY-IFACE)
  ├── grid_composition/
  └── image_variant_management/
  ```

### C-UC-CONTAINER — UseCase コンテナの命名

- UseCase コンテナは **`<Capability>UseCases`** パターンに固定。
  - GRID: `GridCompositionUseCases`
  - IMAGE_VARIANT: `ImageVariantManagementUseCases`
- **`Service` 等の揺れを禁止** (Addendum E の衝突 #6 を消す)。

### C-BOUNDARY-IFACE — 境界インターフェースの型

- cross-Capability の境界は **`src/shared/ports.py` に Protocol で 1 定義**。両側がこれを共有する。
- ImageCopy 存在確認の Port:

  ```python
  # src/shared/ports.py
  import uuid
  from typing import Protocol

  class ImageCopyExistencePort(Protocol):
      def exists(self, copy_id: uuid.UUID) -> bool: ...
  ```

- **戻り値は素の `bool`** (`Result` でラップしない)。引数は `uuid.UUID` (C-IDENTITY)。
- **GRID 側**: UC-05 (`PlaceImageCopy`) は `ImageCopyExistencePort` に依存する。`False` のとき `UnknownCopyId`。
- **IMAGE_VARIANT 側**: `ImageVariantManagementUseCases` は **この Port を満たす** (= `exists(copy_id: uuid.UUID) -> bool` メソッドを公開、内部で UC-16 `ImageCopyExists` を実行)。

> [!IMPORTANT]
> この Port 定義が **アダプタ 0 行の鍵**。step 1 では GRID が `exists(UUID)->bool` を期待し
> IMAGE_VARIANT が `image_copy_exists(str)->Result[bool]` を提供したため、UUID→str と
> Result→bool の 2 段アダプタが必須だった。本契約では **両側が同じ Port を共有** するので
> アダプタは不要になる (はず — これがステップ 2 で実証する命題)。

---

## 2. 横断 MUST_DECIDE の固定 (再発する決定を契約化)

12-must-decide-and-document.md §4.4 が言う「横断的に再発する MUST_DECIDE の昇格先」。
3 回の Phase 2 試行で繰り返し発生した横断決定を本契約で固定する:

| 契約 ID | 決定 | 固定値 |
| --- | --- | --- |
| C-TIMESTAMP | timestamp の時間帯 | **UTC, tz-aware** (`datetime.now(timezone.utc)`) |
| C-REPO-NOTFOUND | Repository の "not found" 表現 | **`None` を返す** (例外を投げない) |
| C-ENUM | Enum 表現 | **Python `enum.Enum`** (Axis, OrderOperation, ScalingMode 等) |
| C-EVENTBUS | EventBus の同期性 | **synchronous in-process** (テスト用 `RecordingBus` を共有) |

> これらは **本契約の管轄**。各 Capability の MUST_DECIDE_AND_DOCUMENT からは外れる
> (= AI が独自決定してはならない)。Capability 固有の MUST_DECIDE (画像 decoder, hash 実装 等) は
> 引き続き各 Capability のローカル決定のまま。

---

## 3. 機械可読インスタンス

```yaml
# 00-convention-contract (machine-readable instance)
contract_version: "0.1"
capabilities: [GRID_COMPOSITION, IMAGE_VARIANT_MANAGEMENT]

identity:
  representation: uuid.UUID          # C-IDENTITY
  factory: uuid.uuid4               # str 変換禁止

shared_value_objects:
  module: src/shared/value_objects.py   # C-SHARED-PLACEMENT
  types: [OccupySize, PixelSize]
  dataclass_options: [frozen]           # C-VALUE-SEMANTICS
  bool_as_int: reject
  min_value: 1

result_wrapper:                       # C-RESULT
  module: src/shared/result.py
  ok_name: Ok
  err_name: Err

module_layout: src                    # C-LAYOUT

naming:
  uc_container: "{Capability}UseCases"  # C-UC-CONTAINER

boundary_ports:                       # C-BOUNDARY-IFACE
  module: src/shared/ports.py
  image_copy_existence:
    protocol: ImageCopyExistencePort
    signature: "exists(copy_id: uuid.UUID) -> bool"
    wrap_in_result: false
    grid_side: "UC-05 depends on ImageCopyExistencePort; False -> UnknownCopyId"
    imgvar_side: "ImageVariantManagementUseCases satisfies the Port (runs UC-16 internally)"

cross_cutting_decisions:              # §2
  timestamp: "UTC, tz-aware"          # C-TIMESTAMP
  repository_not_found: None          # C-REPO-NOTFOUND
  enum_representation: "enum.Enum"    # C-ENUM
  eventbus_sync: synchronous          # C-EVENTBUS
```

---

## 4. 成功判定 (ステップ 2 本体)

本契約の **有効性** は次で判定する:

| 判定項目 | 合格基準 |
| --- | --- |
| **アダプタ行数** | GRID ↔ IMAGE_VARIANT の境界結線に必要な **手書きアダプタ = 0 行** |
| 共有型の同一性 | `OccupySize` が両 Capability で **同一型** (`is` 比較で True) |
| 境界呼び出し | GRID UC-05 が IMAGE_VARIANT の Port を **変換なし** で呼べる |
| 両 Capability のテスト | 各 Capability の必須テスト + Anchor tests が全合格 |
| compose 統合テスト | 「存在する ImageCopy を GRID に配置」「不在の ImageCopy は `UnknownCopyId`」が compose 経由で通る |

> Addendum E ではアダプタが必須だった。本契約導入後に **アダプタ 0 行** を達成できれば、
> Codebase Convention Contract の有効性が実コードで実証される。

---

## 5. 関連

- 方法論側の規範: `../methodology-extensions/21-codebase-convention-contract.md`
- 衝突の実証: `90-feasibility-notes.md` Addendum E
- step 1 の実コード: `../../experiments/phase2-composition-test/`
- 同時生成プロンプト: `41-cocompose-prompt.md`
