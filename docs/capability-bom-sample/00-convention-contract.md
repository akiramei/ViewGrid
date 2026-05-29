# 00 — Codebase Convention Contract (横断規約契約) — GRID_COMPOSITION × IMAGE_VARIANT_MANAGEMENT × RENDERING_EXPORT

> **Status: 具体契約インスタンス v0.3** (方法論本体側の規範は `../methodology-extensions/21-codebase-convention-contract.md` を参照)
> **Scope**: 本契約は `GRID_COMPOSITION` / `IMAGE_VARIANT_MANAGEMENT` / `RENDERING_EXPORT` を **共有契約の下で生成** する際の横断規約。
> **由来**: Addendum E (候補 E ステップ 1) の 6 カテゴリ衝突を消すために制定 (v0.1)。Addendum F (ステップ 2) でアダプタ 0 行を実証。
> **v0.2 の追加**: n=3 スケール検証 (Addendum G) で **消費側 read ポート (§1.8 C-CONSUMER-PORTS)** を追加。
> **v0.3 の変更**: Addendum G の教訓を受け、C-CONSUMER-PORTS を **「最初から必須 (前倒し)」** に格上げ。
> producer は **consumer が存在する前から read 射影を公開** する (= n=2 生成時点で `get_grid_layout` / `get_copy_render_spec` を実装)。
> 目的: 消費側 Capability を後から足したときの **producer retrofit を 0 にできるか** を検証する (Addendum H)。
> 併せて G.7 を受け **C-IDENTITY-BOUNDARY** (出力境界での identity str 化) を §1.9 に追加。

## この契約の目的

Addendum E は、独立生成された 2 実装が **6 カテゴリの規約衝突** により compose 不可だったことを実コードで示した。
本契約は、その 6 カテゴリすべてを **生成前に物理表現レベルで固定** し、
**アダプタ 0 行で複数 Capability が結線できる** ことを目指す。

> [!IMPORTANT]
> 本契約は **物理表現のみ** を規定する。Rule / Decision / Capability 境界の **意味** は
> 各 Capability の 20/21/30 が引き続き権威。本契約はそれらと矛盾してはならない。

> [!NOTE]
> **n=2 (GRID×IMGVAR) は producer→consumer の 1 方向境界** (IMGVAR が ImageCopy 存在を提供、GRID が消費) だった。
> **n=3 では RENDERING_EXPORT が GRID と IMGVAR の *両方を read する* 消費側** となり、新たに 2 本の read 境界が生じる。
> v0.1 の境界 (§1.7 C-BOUNDARY-IFACE) は bool を返す存在確認のみを想定していたため、
> rich な read には §1.8 C-CONSUMER-PORTS を新設する。

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

### C-CONSUMER-PORTS — 消費側 read ポート (v0.3: 最初から必須 / 前倒し)

**消費側 Capability が producer (GRID / IMGVAR) の状態を read** する境界。
v0.1 の C-BOUNDARY-IFACE (bool 返し) では rich な read を表せないため v0.2 で新設し、
**v0.3 で「最初から必須」に格上げ** した。

> [!IMPORTANT]
> **前倒し原則 (v0.3)**: 各 producer Capability は、自分の所有状態の **中立 read 射影** を
> **生成時点 (n=2) から公開** しなければならない — たとえ現時点で consumer が存在しなくても。
> これは「全 Capability は自分の read モデルを中立 DTO で公開する」という一般規約であり、
> 特定 consumer (RENDERING) の都合に合わせるものではない。
> 効果: 消費側 Capability を後から足すとき **producer を一切触らず (retrofit 0)** に結線できる。
> コスト: n=2 が consumer 不在のまま read 射影コードを抱える (投機的)。
> このトレードオフの検証が Addendum H。

**原則**:
- read ポートは **`src/shared/ports.py` に Protocol で 1 定義**。返す型も **`src/shared/` の中立 DTO** とする。
- 消費側 (RENDERING) は **producer の domain 型を import しない** (= GRID の `Placement` / IMGVAR の `ImageCopy` に結合しない)。consumer は shared の中立 DTO のみに依存する。
- producer (GRID / IMGVAR) は **この read ポートを native に満たす** (projection メソッドを自身の UseCases に追加して domain→中立 DTO を写像)。これは n=2 で `ImageVariantManagementUseCases` が `exists()` を native に満たしたのと同じ思想。**standalone アダプタは禁止**。
- 中立 DTO は **producer の enum を持ち込まない**。enum は中立な `str` で表す (例: `scaling_mode: str`)。

**中立 DTO (src/shared/render_contracts.py)**:

```python
import uuid
from dataclasses import dataclass

@dataclass(frozen=True)
class PlacementView:          # GRID Placement の中立射影
    copy_id: uuid.UUID
    x: int; y: int            # セル座標 (x=列, y=行)
    occupy_w: int; occupy_h: int
    order: int                # placement_order (z 順)

@dataclass(frozen=True)
class GridLayout:             # GridCanvas + placements の中立射影
    grid_rows: int; grid_cols: int
    col_weights: tuple[int, ...]; row_weights: tuple[int, ...]
    canvas_w: int; canvas_h: int
    placements: tuple[PlacementView, ...]

@dataclass(frozen=True)
class CopyRenderSpec:         # ImageCopy の中立射影 (R-08 は *未適用*。適用は RENDERING の責任)
    rotation: str             # "None" | "CW90" | "CW180" | "CW270"
    flip_x: bool; flip_y: bool
    scaling_mode: str         # "UniformContain" | "UniformCover" | "Fill"
    alignment: str            # 9 anchor 名
    auto_crop: tuple[int, int] | None        # (target_color_argb, threshold) or None
    manual_crop: tuple[float, float, float, float] | None  # (x, y, w, h) or None
```

**read ポート (src/shared/ports.py)**:

```python
class GridLayoutPort(Protocol):
    def get_grid_layout(self, grid_id: uuid.UUID) -> GridLayout | None: ...

class CopyRenderSpecPort(Protocol):
    def get_copy_render_spec(self, copy_id: uuid.UUID) -> CopyRenderSpec | None: ...
```

- 戻り値は **not-found を `None`** で表す (C-REPO-NOTFOUND と整合。`Result` でラップしない)。
- **GRID 側**: `GridCompositionUseCases` が `get_grid_layout` を native に満たす (GridCanvas + list_placements → `GridLayout`)。
- **IMGVAR 側**: `ImageVariantManagementUseCases` が `get_copy_render_spec` を native に満たす (`ImageCopy` → `CopyRenderSpec`、enum は str 化)。
- **RENDERING 側**: `GridLayoutPort` と `CopyRenderSpecPort` に依存して描画モデルを構築。**R-08 (ManualCropOverridesAutoCrop) の適用点は RENDERING** (manual_crop があれば優先、なければ auto_crop)。

> [!IMPORTANT]
> **v0.2 (後付け) と v0.3 (前倒し) の差**:
> - v0.2: read ポートを後から導入 → n=3 で consumer を足すとき producer に projection を **retrofit** する必要があった (Addendum G。consumer アダプタは 0 だが producer を触る)。
> - **v0.3 (本版)**: read ポートを n=2 生成時点で producer に組み込む → n=3 で consumer を足すとき **producer を一切触らない (retrofit 0)** はず。これが Addendum H で検証する命題。

### C-IDENTITY-BOUNDARY — 出力境界での identity 表現 (v0.3 / G.7 を受けて)

C-IDENTITY は **内部表現は `uuid.UUID` (str 禁止)** を定める。一方、**export / serialization の
出力境界** (例: `RenderDescriptor` の dict / JSON) では **identity を `str` 化** しなければならない。

- 内部 (entity / DTO / port の引数戻り): `uuid.UUID`
- 出力境界 (`to_descriptor()` / JSON 化される dict など): `str(uuid)`
- 根拠: Addendum G.7 で `RenderDescriptor` が生 `uuid.UUID` を含み `json.dumps` 不可だった defect を受ける。
- これは C-IDENTITY (内部) と矛盾しない。**「内部=UUID / 出力境界=str」の 2 面** を明示するもの。

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
contract_version: "0.3"
capabilities: [GRID_COMPOSITION, IMAGE_VARIANT_MANAGEMENT, RENDERING_EXPORT]

identity:
  representation: uuid.UUID          # C-IDENTITY (内部表現)
  factory: uuid.uuid4               # str 変換禁止
  output_boundary: str              # C-IDENTITY-BOUNDARY (v0.3): to_descriptor/JSON 等の出力境界では str(uuid)

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

consumer_ports:                       # C-CONSUMER-PORTS (v0.2 / n=3)
  ports_module: src/shared/ports.py
  dto_module: src/shared/render_contracts.py
  neutral_dtos: [PlacementView, GridLayout, CopyRenderSpec]   # producer enum/domain を持ち込まない
  grid_layout:
    protocol: GridLayoutPort
    signature: "get_grid_layout(grid_id: uuid.UUID) -> GridLayout | None"
    producer: GRID_COMPOSITION   # GridCompositionUseCases が native に満たす (projection 追加可)
  copy_render_spec:
    protocol: CopyRenderSpecPort
    signature: "get_copy_render_spec(copy_id: uuid.UUID) -> CopyRenderSpec | None"
    producer: IMAGE_VARIANT_MANAGEMENT   # ImageVariantManagementUseCases が native に満たす
  consumer: RENDERING_EXPORT     # 中立 DTO のみに依存。R-08 適用点はここ
  standalone_adapter: forbidden
  not_found: None                # Result でラップしない
  baseline_required: true        # v0.3 前倒し: n=2 生成時点で producer が実装する (consumer 不在でも)

cross_cutting_decisions:              # §2
  timestamp: "UTC, tz-aware"          # C-TIMESTAMP
  repository_not_found: None          # C-REPO-NOTFOUND
  enum_representation: "enum.Enum"    # C-ENUM
  eventbus_sync: synchronous          # C-EVENTBUS
```

---

## 4. 成功判定

### 4.1 n=2 (ステップ 2 本体、Addendum F で達成済み)

| 判定項目 | 合格基準 |
| --- | --- |
| **アダプタ行数** | GRID ↔ IMAGE_VARIANT の境界結線に必要な **手書きアダプタ = 0 行** |
| 共有型の同一性 | `OccupySize` が両 Capability で **同一型** (`is` 比較で True) |
| 境界呼び出し | GRID UC-05 が IMAGE_VARIANT の Port を **変換なし** で呼べる |
| 両 Capability のテスト | 各 Capability の必須テスト + Anchor tests が全合格 |
| compose 統合テスト | 「存在する ImageCopy を GRID に配置」「不在の ImageCopy は `UnknownCopyId`」が compose 経由で通る |

> Addendum E ではアダプタが必須だった。本契約導入後に **アダプタ 0 行** を達成できれば、
> Codebase Convention Contract の有効性が実コードで実証される。 → **Addendum F で達成**。

### 4.2 n=3 (RENDERING_EXPORT を Incremental 追加、Addendum G で検証)

| 判定項目 | 合格基準 |
| --- | --- |
| **consumer 結線アダプタ行数** | RENDERING ↔ GRID / RENDERING ↔ IMGVAR の結線に **手書きアダプタ = 0 行** |
| **consumer の domain 非結合** | RENDERING が GRID の `Placement` / IMGVAR の `ImageCopy` を **import しない** (shared 中立 DTO のみ) |
| **producer 追加の種別** | producer に足した結線コードが **native projection (`get_grid_layout` / `get_copy_render_spec`) のみ**。standalone アダプタ 0 |
| **既存 n=2 の非回帰** | committed n=2 の全テストが **green のまま** (RENDERING 追加で壊れない) |
| **R-08 適用** | RENDERING が ManualCropOverridesAutoCrop を適用 (manual_crop 優先)。Anchor test で確認 |
| **render 統合テスト** | grid に配置した copy 群が placement_order の z 順で render モデル化され、crop が R-08 通り解決される |

> **n=3 の問い**: 契約 (n=2 で有効) は **消費側 Capability を 1 つ足したときもアダプタ 0 でスケールするか**。
> producer 側に native projection の後付けが要るなら、それは「契約が read 境界を最初から織り込むべき」という
> 設計示唆 (Addendum G に記録)。

### 4.3 v0.3 前倒し (read ポートを最初から組み込む、Addendum H で検証)

Addendum G の教訓「契約は read 境界を最初から織り込むべき」を実装し、**producer retrofit が 0 になるか** を検証する。

| 判定項目 | 合格基準 |
| --- | --- |
| **n=2 が read ポートを同梱** | 契約 v0.3 下で生成した n=2 (GRID+IMGVAR) が、consumer 不在でも `get_grid_layout` / `get_copy_render_spec` と shared 中立 DTO を **最初から** 持つ |
| n=2 の非退行 | read ポート前倒しを入れても n=2 の existence 境界はアダプタ 0、テスト全合格 |
| **n=3 producer retrofit = 0** | v0.3 n=2 に RENDERING を Incremental 追加したとき、**producer (GRID/IMGVAR) と shared のファイルが byte-identical** (diff 0)。新規追加は `src/rendering_export/` + そのテストのみ |
| consumer 結線アダプタ | RENDERING↔GRID / RENDERING↔IMGVAR とも 0 |
| C-IDENTITY-BOUNDARY | `RenderDescriptor` が identity を str 化し `json.dumps` 可能 (G.7 の解消) |
| トレードオフの観測 | n=2 が consumer 不在のまま read 射影を抱える「投機的コスト」を記録 |

> **H の問い**: read ポートを前倒しすれば、n=3 で **producer を一切触らず** に consumer を足せるか。
> 成立すれば「契約が read 境界を最初から織り込めば、incremental consumer 追加は完全に producer-free」が実証される。

---

## 5. 関連

- 方法論側の規範: `../methodology-extensions/21-codebase-convention-contract.md`
- 衝突の実証: `90-feasibility-notes.md` Addendum E
- step 1 の実コード: `../../experiments/phase2-composition-test/`
- 同時生成プロンプト: `41-cocompose-prompt.md`
