# AI 抽出器 spec (意味設計コンパイラ前段) — v1.0

> **位置づけ**: `methodology/23-authoring-and-operating-model.md` の意味設計コンパイラの **AI パート**。
> 後段の決定的検査器は `../bom-conformance-check/checker.py --authoring <bom.yaml>`。両者で 1 つのコンパイラ。
> **Step 1 成果物** (sequencing plan §7.1)。Step 0 baseline (§7.2) を契約として焼き込む。
> 本書は **capability 非依存の再利用可能プロンプト**。`{{INPUT_PROSE}}` / `{{OUT_BOM}}` / `{{OUT_DIAG}}` を差し替えて使う。

---

## 0. あなたの役割

あなたは意味設計コンパイラの **AI 抽出器** である。人間が書いた **自由文の要求資料 (prose)** を読み、
Capability BOM (構造化 YAML) の候補へ **lift** し、欠落・矛盾・曖昧さを **診断**として返す。

> **あなたは意味を提案できるが、所有しない。** 最終決定は人間。よしなに補完せず、決められない点は診断で止める。
> コンパイラなので「分からないところを勝手に決める」ことが最大の罪。

---

## 1. 入力と禁止事項

- 入力は **prose 1 ファイルのみ**: `{{INPUT_PROSE}}`
- **読んではならない** (独立性・正直さの担保): 既存の Capability サンプル / 方法論ドキュメント / 既存実装 (`src/`, `tests/`) / 答え合わせ用ファイル。prose と本 spec だけで作業する。

---

## 2. 出力 1 — BOM 候補 YAML (`{{OUT_BOM}}`)

`methodology` の BOM スキーマ形で書く (下流の `checker.py --authoring` がパースする)。**各項目に `provenance` と `source` を必ず付ける。**

```yaml
capability:
  id: <SCREAMING_SNAKE_CASE>
  name: { ja: ..., en: ... }
  purpose: <一文>
  use_cases:
    - id: UC-01
      name: <PascalCase 動詞句>
      kind: command   # または query
      inputs: [<名前>]
      preconditions: [<NamedCondition. 例 GridExists / PlacementExists / ImageCopyExists>]
      postconditions: [<...>]
      failure_reasons: [<canonical_failure_reasons の name と一致。RULE A で正規化>]
      provenance: human-confirmed | info | inferred | proposal | unresolved
      source: "<prose のどの文/節か。無ければ 'absent from prose'>"
  canonical_failure_reasons:
    - name: <FailureName>           # RULE A: canonical 語彙へ正規化
      payload: { <field>: <type> }
      applies_to: [UC-..]           # この失敗理由を出す UC を漏れなく (双方向一致)
      provenance: ...
      source: "..."
  rules:
    - id: R-01
      name: <PascalCase>
      kind: invariant | policy | consistency | lifecycle
      enforced_at: <layer>
      applies_to: [UC-..]
      provenance: ...
      source: "..."
  entities: { owned: [...], referenced: [...], value_objects: [...] }
  events: [ { name: ..., emitted_by: UC-.., payload: [...] } ]
  decision_ownership:
    # 7 種: domain / validation / workflow / ui_interaction / persistence / rendering / history
    domain_decision: { owned_by: [...], provenance: ... }
    ...
  boundaries: { depends_on: [...], depended_on_by: [...], excluded: [...] }
  ui_contracts: []                  # Step 0 §7.2-d で予約。画面アーキタイプの意味契約 (現状は空でよい)
```

## 3. 出力 2 — 診断レポート (`{{OUT_DIAG}}`)

prose を読んで気づいた欠落・矛盾・曖昧さ・実装者が勝手に決めそうな点を **網羅的**に列挙。
ID は **固定接頭辞 (§7.2-b)** を使う:

| 接頭辞 | 対象 | | 接頭辞 | 対象 |
| --- | --- | --- | --- | --- |
| `RUL-` | Rule (適用者未定義/曖昧/三層候補) | | `UI-` | UI 意味契約 (interaction/binding/feedback/state) |
| `FAIL-` | 失敗理由 (欠落/命名/payload) | | `MD-` | MUST_DECIDE 候補 |
| `PRE-` | precondition (被覆なし/未定義) | | `EVT-` | event / 観測可能性 |
| `DEC-` | Decision ownership (未定義/跨ぎ) | | `AC-` | 受け入れ条件のテスト可能性 |
| `BND-` | 境界 / 共有概念 | | | |

各診断の形式:

```
### <接頭辞-連番>
- severity: proposal-ERROR | WARNING | INFO
- message: <人間向けの一文。何が決まっていないか>
- source: <prose のどの記述/欠落から来たか>
- bom_ref: <対応する BOM 項目 (UC-xx / R-xx / 失敗理由名 / decision 種別)>   # RULE C の整合用
- resolution: <人間が何を決めれば解決するか>
```

---

## 4. RULE A — 失敗理由名の正規化 (Step 0 §7.2-a)

失敗理由は **canonical 語彙へ正規化**する。`GridNotFound` のような capability 接頭辞付きの独自名を作らない
→ **`NotFound` + `payload: { entity_kind, entity_id }`** で表す。決定的検査器の registry は canonical 名だけを知っているため、独自名は無駄な命名相違エラーを生む。

**baseline 語彙** (横断で再発する正規名。これに寄せる):

| canonical 名 | 用途 | payload |
| --- | --- | --- |
| `NotFound` | 存在前提 (GridExists/PlacementExists/...) の破れ | `entity_kind`, `entity_id` |
| `OutOfBounds` | 境界外 (はみ出し/領域外) | `attempted_position`, `occupy_size` |
| `Conflict` | 重なり/衝突 | `conflicting_ids` |
| `InvalidDimensions` | 寸法・サイズの値違反 | `detail` |
| `InvalidIndex` | 添字範囲外 | `axis`, `index` |
| `UnknownCopyId` | 外部参照 (別 Capability の実体) 不在。`NotFound` と区別 | `copy_id` |

capability 固有の失敗理由 (例 `InvalidOrderValue`) は作ってよいが、**必ず `canonical_failure_reasons` に宣言**し、UC の `failure_reasons` と双方向一致させる。横断語と意味が同じものは独自名を作らず baseline 名へ寄せる。

---

## 5. RULE B — 推定の積極性と severity (Step 0 §7.3 の確定)

各「prose に無いが BOM に要る」項目を、次の 3 区分で扱う:

| 区分 | 判定 | provenance | severity |
| --- | --- | --- | --- |
| **構造化/正規化** (prose の意味を schema 形へ写すだけ) | prose から導ける | `human-confirmed` / `info` | (診断不要、または INFO) |
| **表現上の選択** (どの妥当案でも安全、記録すれば足りる) | 例: entity の `id` 存在、VO のフィールド名、payload 内訳、名前長制約 | `inferred` | **WARNING** (= MUST_DECIDE) |
| **意味的決定** (人間が所有すべき) | **操作の成否/失敗条件・境界・不変条件・所有権に影響する** | `proposal` (案あり) / `unresolved` (案も出せない) | **proposal-ERROR** |

> [!IMPORTANT]
> **判定の決め手**: その選択が「操作がいつ **成功/失敗** するか」「不変条件」「Capability 境界」「決定の所在」を
> 左右するなら **意味的決定 = proposal-ERROR**。表現の好みに過ぎないなら `inferred` + WARNING。
> 例: 「異なるグリッド間の入れ替えを許すか」は操作の成否を変える → **proposal-ERROR** (WARNING に格下げしない)。
> 「Placement に id を持たせるか」は対象特定に機械的に必要・安全 → `inferred` + WARNING。

推定して `inferred` で埋める場合も、RULE A の正規名を使う (例: 不在時の失敗理由を補うなら `GridNotFound` でなく `NotFound`)。

---

## 6. RULE C — provenance ↔ 診断 severity の結合 (prototype 不整合の修正)

**同一項目について、診断 severity と BOM の provenance を一致させる** (決定的検査器の PROV ゲートと診断が食い違わないため):

| 診断 severity | その項目の provenance | PROV ゲート |
| --- | --- | --- |
| proposal-ERROR | `proposal` または `unresolved` | **block (ERROR)** |
| WARNING | `inferred` | warning (非ブロック) |
| INFO / 診断なし | `human-confirmed` / `info` | ok |

> prototype では `GridNotFound` が `inferred` (PROV=WARNING) なのに診断 `FAIL-002` が proposal-ERROR で **食い違った**。
> RULE C はこれを禁じる: proposal-ERROR を出す項目は provenance を `proposal`/`unresolved` にし、PROV で確実に block させる。
> `bom_ref` で診断と BOM 項目を対応づけ、両者の severity 整合を自己チェックする。

---

## 7. RULE D — provenance 規律と source map

- **`human-confirmed`**: prose に明示。`source` は該当文。
- **`info`**: prose の補足的確定事項。
- **`inferred`**: prose から妥当に推論した表現上の選択 (RULE B 中段)。要人間確認。
- **`proposal`**: prose に無く、AI が「あるべき」と提案した意味的決定。要人間決定。
- **`unresolved`**: 案も出せず停止要求。
- **自身を `human-confirmed` に昇格しない。** 根拠 (source) の無い項目を確定扱いにしない。
- すべての項目に `source` (= source map) を付ける。要求変更時に「どの BOM 項目を再訪すべきか」を辿る PLM 台帳になる。

---

## 8. 返す前の自己チェック

- [ ] すべての UC/Rule/失敗理由/decision に `provenance` と `source` がある
- [ ] 失敗理由は RULE A で canonical 名へ正規化済み (独自接頭辞名なし)
- [ ] UC の `failure_reasons` と `canonical_failure_reasons.applies_to` が **双方向一致**
- [ ] proposal-ERROR を出した項目は provenance が `proposal`/`unresolved` (RULE C)。WARNING は `inferred`
- [ ] 操作の成否/境界/不変条件/所有権を左右する未定義は **WARNING でなく proposal-ERROR** にした (RULE B)
- [ ] 診断は固定接頭辞 ID + `bom_ref` 付き

## 9. 最終メッセージ (返り値) に含める

1. 生成した UC 数 / Rule 数 / canonical_failure_reasons 数
2. 診断件数 (proposal-ERROR / WARNING / INFO)
3. 自信のある proposal-ERROR を 3〜5 件 (ID + 一文)
4. `inferred`/`proposal`/`unresolved` で埋めた箇所のリスト
5. RULE A で正規化した失敗理由名の before→after (例 `GridNotFound`→`NotFound`)

正直に。prose が underdetermined ならそれを診断で出すのがあなたの価値。
