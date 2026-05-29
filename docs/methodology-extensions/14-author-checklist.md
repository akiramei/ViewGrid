# 14 — 人間執筆者向け実運用チェックリスト

> **Status: 方法論本体への昇格候補ドラフト**
> 既存 01〜10 が監査者向けに偏っている不足を補う位置づけ

## この文書の目的

既存方法論 01〜10 は **AI を監査者として使う** ことを中心に据えた。
しかし Inverse Audit Protocol (`13-norm-inheritance-and-inverse-audit.md`) を実運用するには、
**人間執筆者が Capability BOM を新規に書く** ためのチェックリストが必要となる。

本文書は、Phase 2 試行 3 回 + 隣接 Capability ドラフトの実体験から導出した、
**Capability BOM の執筆者がチェックすべき項目** を一覧化する。

執筆者は本文書を **手元のチェックリスト** として使い、ドラフト提出前に項目を消し込む。

---

## 1. 全体フロー — 新規 Capability の執筆順

```text
[Step 1] Capability 同定 — 何の意味的能力を切り出すか決める
   ↓
[Step 2] 要求仕様執筆 (10-requirements.md)
   ↓
[Step 3] BOM 執筆 (20-capability-bom.md + 21-yaml)
   ↓
[Step 4] 設計書執筆 (30-design.md)
   ↓
[Step 5] AI プロンプト雛形 (40-ai-implementation-prompt.md)
   ↓
[Step 6] サンプル内整合確認
   ↓
[Step 7] 隣接 Capability との境界整合
   ↓
[Step 8] Phase 2 AI 試行に投入
```

各 Step ごとのチェックリストを以下に示す。

---

## 2. Step 1: Capability 同定チェック

新規 Capability を切り出す前に確認:

- [ ] **意味的能力として独立して語れるか** ("X を可能にする" と一文で言えるか)
- [ ] **既存 Capability に吸収できないか** (重複が大きいなら分割しない)
- [ ] **隣接 Capability との境界を一文で言えるか** ("X は Y を所有しない、参照のみ" 等)
- [ ] **想定 UseCase 数が 5〜20 程度に収まるか** (極端に多いなら分割、極端に少ないなら吸収)
- [ ] **「意味境界」と「Runtime 分割」を混同していないか** (既存 02-core-concepts.md §Capability)

---

## 3. Step 2: 要求仕様執筆チェック (10-requirements.md)

### 3.1 構成チェック

- [ ] §1 背景・目的 (解きたい問題の動機)
- [ ] §2 ペルソナとシナリオ (S1〜S6 程度の典型シナリオ)
- [ ] §3 ユースケース一覧 + 各 UseCase の詳細 (事前/事後/失敗条件)
- [ ] §4 非機能要件 (整合性 / 取消 / 永続化 / 並行性 / 性能 / 国際化)
- [ ] §5 Ubiquitous Language (用語集)
- [ ] §6 受け入れ基準

### 3.2 内容品質チェック

- [ ] **各 UseCase に事前/事後/失敗条件が漏れなく記述されている**
- [ ] **「ユースケース」は UI 部品ではなく操作単位として表現されている** (例: "ボタン押下" は UC ではない)
- [ ] **用語集に登場する語が文書内で同じ意味で使われている**
- [ ] **隣接 Capability で同じ意味の語があれば「権威がどちらか」を明示**
- [ ] **AI が読んで「ここで何を決めるべきか分からない」箇所がない**

### 3.3 アンチパターン

- ❌ UC を「UI イベント」として書く (UC は意味単位、UI は射影)
- ❌ 用語を曖昧に複数の意味で使う
- ❌ 「みんなが知っている」を前提に説明を省略

---

## 4. Step 3: BOM 執筆チェック (20-capability-bom.md + 21-yaml)

### 4.1 構成チェック

- [ ] §1 Capability 同定 (ID, name, purpose, stakeholder)
- [ ] §2 UseCase 一覧 (失敗理由列を含む)
- [ ] §2.1 `canonical_failure_reasons` セクション (v0.2 規範、必須)
- [ ] §3 Rule 一覧 + 各 Rule の保証場所
- [ ] §4 Entities (owned / referenced / value objects)
- [ ] §5 Events
- [ ] §6 Decision Ownership 表
- [ ] §7 Role Taxonomy (allowed / suspicious / forbidden)
- [ ] §8 Capability Boundaries (依存図 + 各境界の説明)
- [ ] §9 観測可能性 (監査要件)
- [ ] YAML 機械可読版が Markdown と整合

### 4.2 `canonical_failure_reasons` 規範

- [ ] **「NotFound」失敗理由がある**:
  - GridExists / PlacementExists / AssetExists / CopyExists 等の preconditions に対応
  - Payload に `entity_kind` を含む
  - 適用 UC を `applies_to` で列挙
- [ ] **失敗理由ごとに payload を明示** (理由名だけでなく内訳)
- [ ] **`applies_to` が各 UC の `failure_reasons` と一致** (cross-reference)

### 4.3 Decision Ownership の規範

- [ ] **7 種類の Decision (domain / validation / workflow / ui_interaction / persistence / rendering / history) のすべてに owned_by を記載**
- [ ] **out_of_scope と forbidden_in を区別して書く**
- [ ] **Capability 跨ぎの Decision (例: cascade_decision) を明示** (上位 Coordinator へ委譲)

### 4.4 Role Taxonomy の規範

- [ ] **allowed roles に各 component (UI / UseCase / Repository 等) を明示**
- [ ] **suspicious と forbidden を分けて書く**
- [ ] **「ImageCopy の意味解釈をする」のような Capability 越境を forbidden に列挙**

### 4.5 Boundaries の規範

- [ ] **依存図 (ASCII 図)** に隣接 Capability を明示
- [ ] **depends_on / depended_on_by を区別**
- [ ] **excluded リストで「本 Capability が扱わないもの」を列挙**
- [ ] **隣接 Capability の Repository / UC のうち本 Capability が呼ぶものを明示**

---

## 5. Step 4: 設計書執筆チェック (30-design.md)

### 5.1 構成チェック

- [ ] §1 Rule Ledger (各 Rule の保証アルゴリズム)
- [ ] §2 Decision Specification (workflow / validation の詳細)
- [ ] §3 Entity の意味的定義
- [ ] §4 Event Catalog
- [ ] §5 Persistence Boundary (Repository インターフェース宣言、cascade decision の所在)
- [ ] §6 テスト戦略 (必須カテゴリ + property-based 必須)
- [ ] §7 Worked Examples (W-1〜W-6 程度)
- [ ] §8 Anchor Tests (AT-01〜AT-10 程度)
- [ ] §9 実装非規定事項 (AI 自由度 + 不変更項目)

### 5.2 三層構造の適用判定 (11-three-layer-disambiguation.md §7)

各 Rule / UC について:

- [ ] 単層 (narrative のみ) で AI が一意解釈できるか?
- [ ] Yes → 単層で OK
- [ ] No → 三層 (narrative + algorithmic + executable) を適用
  - [ ] narrative: §1 R-XX NOTE / §2.2 / §7 Worked Example
  - [ ] algorithmic: §2.2 workflow_decision の手順
  - [ ] executable: §8 AT-XX Anchor Test

#### 三層を必ず適用すべき場面 (再掲)

- [ ] Capability 境界に跨る Rule
- [ ] エッジケースで実装が分かれやすい不変条件
- [ ] 「直感的に綺麗そう」が誤った実装になる場合
- [ ] 複数の妥当な解釈がある手順

### 5.3 Property-based test の規範

- [ ] **1000-step random walk を必須テストカテゴリに記載**
- [ ] 検出すべき invariant を列挙 (R-XX が常に成立、等)
- [ ] seed 固定で再現可能なテストを要求

### 5.4 Anchor Tests の規範

- [ ] AT-01 〜 AT-10 程度を **テスト関数名検索可能な形** で記述
- [ ] **各 AT-XX に対応する W-XX (Worked Example) がある**
- [ ] AT には happy path / failure path / edge case の組み合わせを含む
- [ ] 「期待振る舞い」が一文で書ける

---

## 6. Step 5: AI プロンプト雛形チェック (40-ai-implementation-prompt.md)

既存 09-ai-audit-prompt-guide.md の 8 構造 + 第三カテゴリ (12-must-decide-and-document.md):

- [ ] §A 完全版プロンプト (コピペ可能な形)
- [ ] INPUT DOCUMENTS で正準入力を明示
- [ ] GOAL で実装目標を一文で
- [ ] SCOPE で対象 Capability を限定
- [ ] **NON-GOALS で「コード修正」「リファクタ提案」「Rule 名変更」等を明示**
- [ ] CAPABILITY CONTEXT で意味境界を要約
- [ ] **ALLOWED (自由) を網羅**
- [ ] **`MUST_DECIDE_AND_DOCUMENT` 第三カテゴリで典型決定 ≥ 5 件を例示** (12)
- [ ] FORBIDDEN で ID 系の不変更項目を明示
- [ ] OUTPUT FORMAT で実装一式 + 実装ノート + README を要求
- [ ] **CONFIDENCE POLICY で unclear / suspected 表現を許容**
- [ ] **POST_IMPLEMENTATION_SELF_AUDIT で項目数を明示**
  - 数字 (六項目 / 七項目) と本文の項目数が **一致** することを確認 (= typo 防止)

---

## 7. Step 6: サンプル内整合確認チェック

サンプル内で Markdown / YAML / 用語集が整合しているか:

- [ ] **YAML と Markdown の UseCase 一覧 + 失敗理由が一致**
- [ ] **YAML と Markdown の Rule 一覧 + 保証場所が一致**
- [ ] **`canonical_failure_reasons.applies_to` と各 UC の `failure_reasons` が一致** (cross-reference)
  - [ ] 機械照合 **C3** (`22-bom-conformance-check`) を回し、drift 0 を確認 (人手では取りこぼす。I-C3a/b/c の実例)
- [ ] **自己検証 VO が保証する失敗理由は `guaranteed_by` を注記** (C1 が upstream ガードを検証。F-1/F-2)
- [ ] **宣言した precondition に対応する失敗理由が canonical にある** (C2 で強制を検証。B-D3)
- [ ] **用語集の語が Markdown 全体で同じ意味で使われている**
- [ ] **README の構成説明と実ファイルが一致**
- [ ] **フォワードリファレンス禁止**: 「§Y を参照」と書いた箇所の §Y が既に存在
- [ ] **`六項目` / `七項目` 等の数字付き表現の数字と本文項目数が一致**

矛盾発見時のルール (09 既存規範に従う):

- YAML が正準
- Markdown は YAML に整合させる
- 矛盾を直したら CHANGELOG に記録

---

## 8. Step 7: 隣接 Capability との境界整合チェック

2 つ以上の Capability サンプルが共存する場合:

- [ ] **共有値オブジェクト (`OccupySize`, `PixelSize` 等) の権威が決まっている**
  - どちらか一方の Capability が「共有定義」を所有
  - 他方は「`X` と共有定義」と明示
- [ ] **Cross-Capability の UC 呼び出し関係が両側に記載されている**
  - 呼ぶ側: 「`YYY` の UC-XX を呼ぶ」
  - 呼ばれる側: 「cross-Capability 用」と明示
- [ ] **Capability 跨ぎの Rule (例: R-08 ManualCropOverridesAutoCrop) の保証 Capability が決まっている**
  - Declaration-only として記載する Capability と、適用する Capability を明示
- [ ] **Cascade decision の所在が明示されている**
  - 「本 Capability では cascade しない」を明文化
  - 上位 Coordinator が決定する場合、その旨を明示

---

## 9. Step 8: Phase 2 AI 試行投入前最終チェック

Phase 2 試行 (13-norm-inheritance-and-inverse-audit.md §2) に投入する前:

- [ ] 全ファイルが UTF-8 (or 規定エンコード) で保存されている
- [ ] AI への入力ドキュメントが揃っている (10/20/21/30/40 + README)
- [ ] 既存実装参照禁止の制約をプロンプトに記載
- [ ] 出力先 (`experiments/phase2-<capability-id>-v<X>-impl/`) を指定
- [ ] 報告期待項目 (unclear / overreach / MUST_DECIDE / 自己監査 / 主観評価) を明示
- [ ] **受け入れゲートの明示**: プロンプトの POST_IMPLEMENTATION_SELF_AUDIT に
      「BOM↔実装 照合 (`22`/checker.py) を回し **GATE: PASS (exit 0)** を確認してから完了報告」を含める
      (= drift を **コミット前** に弾く shift-left。事後発見の手戻りを断つ)
- [ ] **本チェックリストの §1〜§7 を一通り消化済み**

---

## 10. 副パターンの引用 (将来の 15〜20 向け)

本ディレクトリでは詳細化していないが、執筆者が遭遇する副パターン:

### 10.1 Anchor Tests Spec (将来の 15-)

Anchor Test の網羅性が問題になった場合:

- 現状の AT-01〜AT-10 は **代表値**。全 UC × 全 failure_reason の組み合わせを網羅するには 30〜50 件必要
- Phase 2 v0.2 時点では 10 件で実用十分。Capability の複雑度が増したら拡張する

### 10.2 Coordinator Pattern (将来の 16-)

2 Capability 以上が連携する場合、**Capability 外の調停層** が必要:

- 本 Capability は cascade decision を持たない
- 上位 Coordinator が UC を組み合わせて全体フローを構築
- 例: `ImageAsset 削除` を「全派生物削除 → 元画像削除」の 2 段で実行

### 10.3 Declaration-only Rules (将来の 17-)

ある Rule が複数 Capability に跨る場合:

- 各 Capability の Rule ledger に **記載するが保証コードは持たない**
- 保証は別 Capability で行う (例: R-08 は本 Capability では宣言のみ、RENDERING_EXPORT で適用)

### 10.4 Shared Concepts Schema (将来の 18-)

共有値オブジェクトを BOM スキーマに正式化:

```yaml
shared_concepts:
  - name: OccupySize
    authority: GRID_COMPOSITION
    used_by: [GRID_COMPOSITION, IMAGE_VARIANT_MANAGEMENT]
```

### 10.5 Cross-Capability Naming Convention (将来の 19-)

存在性確認 UC の命名規範:

- パターン: `<EntityName>Exists` (例: `ImageCopyExists`, `GridCanvasExists`)
- 戻り値: `bool` (失敗理由なし)

### 10.6 Revision Checklist (将来の 20-)

改訂作業中の取りこぼし防止:

- [ ] フォワードリファレンスを書いたら本体も書く (Addendum B の D-2 教訓)
- [ ] 失敗理由を改名・追加したら YAML / Markdown / canonical_failure_reasons の 3 箇所を同時更新
- [ ] 「六項目」「七項目」等の数字表現と本文項目数の整合 (Addendum D の E-1 教訓)
- [ ] CHANGELOG.md でバージョン差分を追跡

---

## 11. このチェックリストの想定運用

執筆者は次の頻度で本チェックリストを使う:

- **新規 Capability ドラフト時**: Step 1〜8 を一通り
- **改訂時**: 変更箇所に関連する Step のみ
- **Phase 2 投入直前**: §9 を必ず

完全消化に Capability 1 つあたり 4〜8 時間程度を想定 (PoC での GRID v0.2 改訂と整合)。

---

## 12. 採用判定

| 評価軸 | 結果 |
| --- | --- |
| 実証根拠 | Phase 2 試行 3 回 + 隣接 Capability ドラフトでの実体験 |
| 適用コスト | 中 (1 Capability あたり 4〜8 時間のチェック作業) |
| 既存方法論との整合 | 補完関係 (既存は監査者向け、本書は執筆者向け) |
| 認知負荷 | 高 (項目数が多い) — ただしステップ分割で軽減 |

---

## 13. 関連ドキュメント

- 11-three-layer-disambiguation.md — Step 5 §5.2 の三層適用判定の根拠
- 12-must-decide-and-document.md — Step 6 の MUST_DECIDE_AND_DOCUMENT 規範の根拠
- 13-norm-inheritance-and-inverse-audit.md — Step 8 投入先の Phase 2 プロトコル
- 実証根拠: `docs/capability-bom-sample/90-feasibility-notes.md` Addendum A / B / C / D
- 実例: `docs/capability-bom-sample/` (GRID v0.2) と `docs/capability-bom-sample/image-variant-management/` (IMAGE_VARIANT v0.1)
