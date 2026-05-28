# 21 — Codebase Convention Contract (横断規約契約)

> **Status: 方法論本体への昇格候補ドラフト** (内部候補 ID: G-1)
> 既存 01〜10 にない新規概念。複数 Capability の **合成可能性** を保証する上位レイヤ
> 由来: `90-feasibility-notes.md` Addendum E (候補 E ステップ 1 — 複数 Capability 合成試行)
>
> **番号について**: 本来は副候補 18 (Shared Concepts Schema) と対になる **物理レイヤ** の文書。
> 18 が未ドラフトのため、本書を先行ドラフトとして 21 に置く。昇格時に 18 とセットで再番号付けしてよい。

## この文書の目的

13-norm-inheritance-and-inverse-audit.md は **規範継承性** — 方法論の規範が整えば
新規 Capability の v0.1 段階から実用品質に到達できる — を実証した。

しかしこの規範継承は **Capability *内部* の品質** のみを継承する。
**Capability *間* のコード規約** (identity の物理表現、共有型の配置、Result ラッパの命名など) は
各実装セッションの自由裁量に委ねられ、合成時に衝突する。

本文書は、サンプル成果物 (Capability 単位) より **上位のレイヤ** に置く
**Codebase Convention Contract (横断規約契約)** を導入する。
これは Addendum E で **実コードによって実証された** 新規概念。

---

## 1. 動機 — 規範継承だけでは合成できない

### 1.1 Addendum E が示した事実

候補 E ステップ 1 では、独立に生成された 2 実装
(GRID v0.2 + IMAGE_VARIANT v0.1) を 1 プロセスに同居させ、境界結線を試みた。

結果:

> **coexist (同居) は可能、compose (直接合成) は不可。**

2 パッケージは import 衝突なく同居できたが、境界を直接結線できず、
6 カテゴリの規約不整合を埋める **手書きアダプタ** が必要だった。

### 1.2 観測された 6 カテゴリの規約衝突

| # | カテゴリ | GRID v0.2 | IMAGE_VARIANT v0.1 | 衝突の性質 |
| --- | --- | --- | --- | --- |
| 1 | モジュールレイアウト | flat (ルート直下 `grid_composition/`) | `src/` layout | パッケージ発見規約が不一致。sys.path 2 種類 |
| 2 | 共有値オブジェクト型 | `grid_composition...OccupySize` (`frozen=True`) | `...shared.OccupySize` (`frozen=True, slots=True`) | **別モジュール定義 = 別型**。`is` も `==` も False |
| 3 | 値オブジェクトの bool 検証 | `OccupySize(True,1)` を拒否 | `OccupySize(True,1)` を許容 | エッジケースの契約が異なる |
| 4 | identity 表現 | `uuid.UUID` オブジェクト | `str` (`str(uuid.uuid4())`) | 境界で UUID ↔ str 変換が必要 |
| 5 | Result ラッパ命名 | `Ok` / `Err` | `Ok` / `Failure` | 失敗ラッパ名が違う。`Ok` も別モジュール = 別型 |
| 6 | UC コンテナ命名 | `GridCompositionUseCases` | `ImageVariantManagementService` | BOM から物理命名を予測不能 |

これらは **すべて規範継承の外側** にある。サンプル成果物 (10/20/21/30/40) は
Capability 内部の品質規範 (canonical_failure_reasons, Anchor tests, 三層構造) を継承させたが、
**横断的なコード規約は各実装者の自由裁量のまま** だった。

### 1.3 メタ観測 — 第三者は物理命名を BOM から予測できない

合成スクリプト執筆時、UC コンテナを `ImageVariantManagementUseCases` と推測したが、
実際は `ImageVariantManagementService` で **ImportError** が発生した。

> BOM (20/21) には「UseCase を提供する」と書かれているが、それを束ねるクラスの命名は
> 実装者の自由裁量であり、第三者は BOM だけからは予測できない。
> cross-Capability 結線には **「インターフェースの物理的な形 (型・名前)」の契約** が要る。

### 1.4 結論

Capability BOM が「何を意味するか (semantic)」を規定しても、
「それをどう物理表現するか (physical)」が Capability 間で揃わなければ、
**実装は割れて合成できない**。

「規範継承の外側にある横断規約」を明示的に固定する契約レイヤが必要 — これが Codebase Convention Contract。

---

## 2. Codebase Convention Contract の定義

### 2.1 定義

```text
Codebase Convention Contract (横断規約契約):
  複数 Capability の実装が合成可能であるために、
  すべての Capability 実装セッションが従う共通のコード規約。
  サンプル成果物 (Capability 単位) より一段上のレイヤに 1 つだけ存在する。
```

| 性質 | 内容 |
| --- | --- |
| スコープ | プロジェクト横断 (全 Capability 共通) |
| 配置 | サンプル成果物の **上位** (`docs/capability-bom-sample/00-convention-contract.md` 等、1 ファイル) |
| 拘束力 | 各 Capability の 40-prompt から **参照され、FORBIDDEN 相当の拘束** を持つ |
| 充足対象 | 物理表現 (型・配置・命名・レイアウト)。意味 (Rule/Decision) は対象外 |

### 2.2 Shared Concepts (18) との層分離 — 最重要

Codebase Convention Contract は副候補 18 (Shared Concepts Schema) と **対になる別レイヤ**:

| レイヤ | 文書 | 扱う問い | 例 |
| --- | --- | --- | --- |
| **semantic (概念)** | 18 Shared Concepts Schema | *どの概念* を Capability 間で共有するか | 「`OccupySize` は GRID と IMAGE_VARIANT で同じ概念」 |
| **physical (物理表現)** | **21 Codebase Convention Contract (本書)** | 共有する概念を *どう物理表現* するか | 「`OccupySize` は `shared/value_objects.py` に 1 定義、`frozen=True`、`bool` を拒否」 |

> [!IMPORTANT]
> **後者 (本書) がなければ、前者 (18) で「共有する」と宣言しても実装が割れる。**
> Addendum E はまさにこれを実証した: ドキュメント上で「共有値オブジェクト」と宣言していたが、
> 物理配置を契約していなかったため、各実装が独立に別型を作り、実行時に交換不能になった。

18 が「共有する概念のカタログ」を定め、本書が「そのカタログの各概念に物理表現を 1 つ割り当てる」。
両者はセットで運用する。

---

## 3. 契約項目 (Contract Items)

Addendum E §E.6 で導出した最低限の契約項目。各 Capability の実装はこれに **逐一従う**。

### 3.1 必須契約項目

| 契約項目 | 規定すべきこと | サンプルでの推奨値 |
| --- | --- | --- |
| **C-IDENTITY** | identity の物理型 | 全 Capability で `uuid.UUID` に統一 (or 全て `str`)。混在禁止 |
| **C-SHARED-PLACEMENT** | 共有値オブジェクトの物理配置 | `shared/value_objects.py` に 1 定義。全 Capability が import (局所複製禁止) |
| **C-VALUE-SEMANTICS** | 共有値オブジェクトのコンストラクタ契約 | `frozen=True`、`bool` を `int` として拒否、等を 1 つに固定 |
| **C-RESULT** | Result / 失敗ラッパの命名と配置 | `Ok` / `Err` を共有モジュールに 1 定義。`Failure` 等の同義語を禁止 |
| **C-LAYOUT** | モジュールレイアウト | `src/` layout か flat か統一。パッケージ発見規約を 1 つに |
| **C-UC-CONTAINER** | UseCase コンテナの命名パターン | `<Capability>UseCases` 等のパターンを固定 (`Service` 等の揺れを禁止) |
| **C-BOUNDARY-IFACE** | 境界インターフェースの型 (producer→consumer の存在確認等) | 存在確認は `exists(id) -> bool` に統一 (`Result` でラップしない等) |
| **C-CONSUMER-PORTS** (n=3 で追加) | 消費側 Capability が producer を **read** する境界 | read ポート + **中立 DTO** を `shared/` に 1 定義。consumer は producer domain を import しない。producer は native projection で満たす |

> [!IMPORTANT]
> **C-BOUNDARY-IFACE は producer→consumer の 1 方向 (bool 返し) しか想定していなかった**。
> n=3 (Addendum G) で **消費側 Capability (RENDERING_EXPORT) が 2 つの producer を read** したとき、
> rich な read を表す **C-CONSUMER-PORTS** が必要になった。
> 教訓: **契約は read 境界を *最初から* 織り込むべき**。さもないと消費側を後から足したとき、
> 凍結 producer に projection を retrofit する羽目になる (consumer 結線アダプタは 0 を保てるが producer を触る)。
> 詳細は `docs/capability-bom-sample/00-convention-contract.md §1.8` と Addendum G。

### 3.2 横断 MUST_DECIDE_AND_DOCUMENT の昇格先としての役割

12-must-decide-and-document.md §4.4 は次を述べる:

> 同じ典型決定が 2 回以上発生 → サンプル v0.X+1 で明示的に決める価値あり (FORBIDDEN へ移動)

ここで決定が **横断的 (Capability 間の接続に影響する)** な場合、その昇格先は
単一 Capability の BOM ではなく **本契約** である。

Phase 2 試行 3 回で繰り返し観測された MUST_DECIDE_AND_DOCUMENT のうち、横断的なもの:

| 横断 MUST_DECIDE 項目 | 観測 | 本契約での扱い |
| --- | --- | --- |
| timestamp の UTC / local | 3 回全てで発生 | **C-TIMESTAMP** として契約化 (例: UTC, tz-aware) |
| Repository "not found" の None / 例外 | 3 回全てで発生 | **C-REPO-NOTFOUND** として契約化 |
| Enum vs 文字列定数 | 複数回 | **C-ENUM** として契約化 |
| EventBus の同期性 | 複数回 | **C-EVENTBUS** として契約化 |
| identity 表現 | 合成時に衝突 (E) | C-IDENTITY (§3.1) |
| 共有値オブジェクト配置 | 合成時に衝突 (E) | C-SHARED-PLACEMENT (§3.1) |

> **つまり Codebase Convention Contract は「横断的に再発する MUST_DECIDE_AND_DOCUMENT の最終的な棲み家」** でもある。
> Capability 固有の MUST_DECIDE (例: 画像 decoder 選択、hash 実装) は本契約には昇格させない — それらは各 Capability のローカル決定のまま。

### 3.3 契約テンプレート (執筆者が埋める雛形)

```yaml
# 00-convention-contract.yaml — プロジェクト横断のコード規約契約 (1 ファイルのみ)
contract_version: "0.1"

identity:
  representation: uuid.UUID        # C-IDENTITY: 全 Capability 共通。混在禁止

shared_value_objects:
  placement: shared/value_objects  # C-SHARED-PLACEMENT: 1 箇所に定義
  dataclass_options: [frozen]      # C-VALUE-SEMANTICS
  bool_as_int: reject              # bool を int として拒否するか

result_wrapper:
  ok_name: Ok                      # C-RESULT
  err_name: Err                    # Failure 等の同義語を禁止
  module: shared/result

module_layout: src                 # C-LAYOUT: "src" | "flat"

naming:
  uc_container: "{Capability}UseCases"   # C-UC-CONTAINER

boundary_interfaces:
  existence_check: "exists(id) -> bool"  # C-BOUNDARY-IFACE: Result でラップしない

cross_cutting_decisions:           # §3.2 — 横断 MUST_DECIDE の昇格先
  timestamp: "UTC, tz-aware"       # C-TIMESTAMP
  repository_not_found: "None"     # C-REPO-NOTFOUND
  enum_representation: "Enum"      # C-ENUM
  eventbus_sync: "synchronous"     # C-EVENTBUS
```

このファイルは **プロジェクトに 1 つだけ**。新 Capability を追加するたびに更新するのではなく、
全 Capability がこれを **前提** として実装する。

---

## 4. 運用規範

### 4.1 実装プロンプト (40-ai-implementation-prompt.md) への組み込み

各 Capability の 40-prompt に、本契約を参照する **新セクション** を設ける。
これは ALLOWED / MUST_DECIDE_AND_DOCUMENT / FORBIDDEN のいずれとも異なる
**横断拘束 (cross-cutting binding)** として位置づける:

```text
== CODEBASE_CONVENTION_CONTRACT (横断規約契約 — 全 Capability 共通、変更不可) ==
本実装は 00-convention-contract.yaml に従うこと。以下は FORBIDDEN 相当の拘束:

- identity は uuid.UUID で表現する (str に変換しない)
- 共有値オブジェクト (OccupySize / PixelSize) は shared/value_objects から import する
  (局所複製・再定義は禁止)
- Result は Ok / Err を shared/result から import する (Failure 等の別名を作らない)
- モジュールレイアウトは src/ layout
- UseCase コンテナは <Capability>UseCases と命名する (Service 等の揺れ禁止)
- 境界の存在確認は exists(id) -> bool で公開する (Result でラップしない)

これらは MUST_DECIDE_AND_DOCUMENT ではない。AI が独自決定してはならない。
```

### 4.2 単一 Capability 試行では適用不要

Codebase Convention Contract は **複数 Capability を合成する時のみ** 意味を持つ。
単一 Capability の Phase 2 試行 (Addendum A/B/D) では適用不要。
過剰に早く導入すると、単一 Capability の実装自由度を不必要に制約する。

> 適用の閾値: **「2 つ目以降の Capability を、1 つ目と合成する意図がある時」**。

### 4.3 契約のバージョニング

- 契約は `contract_version` を持ち、全 Capability 実装がどの契約版に準拠したかを記録する
- 契約変更は **全 Capability の再合成検証** を要求する破壊的変更になり得る
- 13 のバージョニング規範 (意味的バージョニング) に準じる

---

## 5. ステップ 2 への含意 — 「事後合成」から「同時生成」へ

Addendum E §E.7 の最大の設計示唆:

> 「独立生成物の事後合成」は否定された。
> ステップ 2 は「**共有契約下での同時生成**」に設計変更すべき。

本契約はこの設計変更の **前提条件** である。ステップ 2 の手順:

```text
[ステップ 2 前処理]
  1. Codebase Convention Contract (本書 §3.3 の雛形) を執筆
  2. 各 Capability の 40-prompt に §4.1 の CODEBASE_CONVENTION_CONTRACT セクションを組み込む

[ステップ 2 本体]
  3. 複数 Capability を 1 つの Phase 2 試行で *同時生成* させる
     (独立生成 → 後で合成、ではなく、最初から共有契約下で生成)
  4. 合成 (compose) が手書きアダプタなしで成立するかを検証
     = 本契約が 6 カテゴリの衝突を事前に消せたかの実証
```

ステップ 2 の成功判定は **「アダプタ 0 行で 2 Capability が結線できるか」**。
Addendum E ではアダプタが必須だった。本契約導入後にアダプタ 0 行を達成できれば、
**Codebase Convention Contract の有効性が実コードで実証** される。

---

## 6. アンチパターン

### 6.1 契約を Capability ごとに分散させる

各 Capability の BOM に「うちはこう書く」と分散させると、結局権威が曖昧化して衝突する。
契約は **プロジェクトに 1 ファイル**。

### 6.2 契約を意味レイヤ (Shared Concepts / Rule) と混同する

本契約は **物理表現のみ** を扱う。「`OccupySize` とは何か」は 18 (Shared Concepts) と Rule の領分。
「`OccupySize` をどこに置きどう書くか」だけが本契約。混ぜると両方が肥大化する。

### 6.3 単一 Capability 段階で導入する

合成意図がないうちに導入すると、実装自由度を奪うだけで利得がない (§4.2)。

### 6.4 Capability 固有決定まで契約に昇格させる

画像 decoder 選択や hash 実装のような **Capability ローカルな MUST_DECIDE** を契約に入れると、
契約が特定 Capability に依存し、横断契約の意味を失う。横断的な決定のみを昇格させる (§3.2)。

---

## 7. 既存方法論本体への接続

| 既存 / 拡張文書 | 本契約との接続 |
| --- | --- |
| 02-core-concepts.md | Capability の物理境界 (型・配置) を扱う新レイヤを追加 |
| 09-ai-audit-prompt-guide.md | プロンプトに CODEBASE_CONVENTION_CONTRACT 横断拘束セクションを追加 |
| 12-must-decide-and-document.md | 横断的に再発する MUST_DECIDE の昇格先を本契約として明示 |
| 13-norm-inheritance-and-inverse-audit.md | 規範継承が **届かない範囲** (Capability 間規約) を本契約が補完 |
| 18 Shared Concepts Schema (副候補) | semantic レイヤ。本契約 (physical) と対をなす (§2.2) |
| 16 Coordinator Pattern (副候補) | Coordinator が境界結線する際、本契約が結線対象の物理形を保証する |

---

## 8. 採用判定

| 評価軸 | 結果 |
| --- | --- |
| 実証根拠 (必要性) | Addendum E で 6 カテゴリの衝突を実コードで観測。契約の必要性は確定 |
| 実証根拠 (有効性 / n=2) | **Addendum F でアダプタ 0 行・101 テスト合格を実コードで達成** |
| 実証根拠 (スケール / n=3) | **Addendum G で消費側 Capability (RENDERING) を Incremental 追加。consumer 結線アダプタ 0・140 テスト合格 (n=2 由来 101 非回帰)・R-08 適用を実コードで達成** |
| 適用コスト | 中 (契約 1 ファイルの執筆 + 各 40-prompt への参照追加) |
| 既存方法論との整合 | 補完関係 (規範継承が届かない範囲を埋める)。18 とセット運用 |
| 認知負荷 | 低 (契約は 1 ファイル、項目は §3.1 の 8 + §3.2 の横断 MUST_DECIDE) |
| 残課題 (F-1) | 契約 (physical) が Capability ローカル失敗理由 (semantic) を到達不能にしうる。層間整合チェックが要る |
| 残課題 (G) | 契約は **read 境界 (C-CONSUMER-PORTS) を最初から織り込むべき**。さもないと消費側追加時に producer retrofit が要る (consumer アダプタは 0 を保てる) |

> [!NOTE]
> 本書は **必要性 (E)・有効性 (F)・n=3 スケール (G) が実コードで実証済み** の段階。
> 残課題は F-1 (physical/semantic 層間整合) と G (read 境界の前倒し)。

---

## 9. 関連ドキュメント

- 12-must-decide-and-document.md — 横断的に再発する MUST_DECIDE の昇格先が本契約
- 13-norm-inheritance-and-inverse-audit.md — 規範継承が届かない範囲を本契約が補完
- 副候補 18 (Shared Concepts Schema) — 本契約 (physical) と対をなす semantic レイヤ
- 副候補 16 (Coordinator Pattern) — 境界結線層。本契約が結線対象の物理形を保証
- 実証根拠 (必要性): `docs/capability-bom-sample/90-feasibility-notes.md` Addendum E
- 実証根拠 (有効性 / n=2): 同 Addendum F (アダプタ 0 行・101 テスト合格)
- 実証根拠 (スケール / n=3): 同 Addendum G (consumer アダプタ 0・140 テスト合格・R-08 適用)
- 具体契約インスタンス: `docs/capability-bom-sample/00-convention-contract.md` (v0.2、§1.8 C-CONSUMER-PORTS)
- 同時生成プロンプト: `docs/capability-bom-sample/41-cocompose-prompt.md` (n=2) / `42-rendering-incremental-prompt.md` (n=3)
- RENDERING サンプル: `docs/capability-bom-sample/rendering-export/`
- step 1 の実コード: `experiments/phase2-composition-test/` (compose 不可の実証)
- step 2 の実コード: `experiments/phase2-cocompose-impl/` (n=2、アダプタ 0 行で compose 可)
- step 3 の実コード: `experiments/phase2-n3-incremental-impl/` (n=3、consumer アダプタ 0)
