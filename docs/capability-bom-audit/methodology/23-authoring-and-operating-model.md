# 23 — Authoring & Operating Model (人間の意味資料 → 意味設計コンパイラ → AI実装 の運用モデル)

> **Status: draft (統合ブループリント)**。既存 01〜22 の **上に乗る Authoring/Operating 層** の全体像を 1 枚に固定する。
> 本書で骨格を確定したのち、(b) 意味設計コンパイラ仕様 / (d) UI 意味契約 等は個別の詳細文書へ分割する想定 (番号は §7 で扱う)。
> **由来**: 候補 E 完了 (Addendum E〜J) 後の方向づけ議論。「人間はコードを管理せず、意味の BOM を管理する」という分業を **工程・ツール・責任** のレベルまで具体化する。

---

## 0. 一文要約と立ち位置

> 既存 01〜22 は **「AI に BOM から実装させ、生成物を監査する」内側の方法論** である。
> 本書はその **外側の運用モデル**: **人間は意味資料だけを管理し、AI がそれを compile・実装・照合する**。
> 人間はコードを統制しない。**意味資料と、その compile 結果 (BOM / 診断) を統制する**。

この層を入れることで、繰り返し問われる疑問「人間がコードを見ないのにどう統制するのか?」に答えられる:

> **人間はコード構造 (architecture style) を統制するのではなく、意味 (semantic governance) を統制する。**
> AI は compile 済みの意味契約の下で実装し、ツールが BOM↔実装を照合する。

---

## 1. 二層アーキテクチャ — Human-facing / AI-facing compiled

| 層 | 誰が書くか | 形式 | 中身 |
| --- | --- | --- | --- |
| **Human-facing layer** | **人間 (主) + AI 補助** | 文章中心でよい | 要求、背景、ユーザーシナリオ、用語、業務制約、業務判断、画面意図、未決事項 |
| **AI-facing compiled layer** | **AI (compile) + 人間承認** | 構造化 (Markdown/YAML) | Capability BOM / Rule Ledger / Decision Ownership / canonical_failure_reasons / Anchor Tests / 横断規約契約 (21) / 実装プロンプト |

> [!IMPORTANT]
> **人間は AI-facing layer を直接書かない。** 人間が最初から BOM/YAML/Decision 表を手書きする運用は負荷が高すぎる
> (既存 `14-author-checklist.md` は AI-facing 成果物を **受動的に消し込む** チェックリストであり、これとは別物)。
> 人間は人間向けの意味資料を書き、**意味設計コンパイラがそれを AI-facing layer へ変換する**。主は人間、AI は大きな補助。

この二層分離が方法論の思想的な芯である:
- **人間が責任を持つのは「意味」**。長期的に守るべきは実装の形ではなく、BOM に記録された意味。
- **コードはその時点の実装インスタンス**。コード構造は AI の実装自由度として扱う。

---

## 2. shift-left の連続体 — 同じ「BOM↔実体のズレ検出」を時間軸で 3 段に伸ばす

本層の中核 (意味設計コンパイラ) は新発明ではなく、既存の照合機構を **さらに左 (コードが存在する前)** へ倒したものである。

```text
人間向け資料 ──[① 意味設計コンパイラ]──→ AI向けBOM ──[② 照合ゲート]──→ 実装 ──[③ 事後監査]──→ BOM視点レビュー
              authoring-time diagnostics      generation-time gate         post-gen audit
              (本書 = 新規)                    (22 + checker.py = 実証済)    (01-10 = 実証済)
```

| 段 | チェックポイント | 既存/新規 | 何を弾くか |
| --- | --- | --- | --- |
| ① | 意味設計コンパイラ (authoring 時) | **新規 (本書)** | 人間資料の曖昧さ・欠落・矛盾。コードが書かれる前に止める |
| ② | 照合ゲート (生成の受け入れ時) | 実証済 (`22` + `checker.py`、Addendum I/J) | BOM 宣言と実装のズレ。コミット前に弾く |
| ③ | 事後監査 (生成後) | 実証済 (`01〜10`、特に `09`) | Decision overreach。意味境界の越境 |

> Addendum J は ② を「コミット後 → 生成の受け入れゲート」へ前倒しした。本書の ① は **同じ思想を BOM 執筆時点まで前倒し** する。3 段は連続体であり、左ほど手戻りが安い。

---

## 3. 意味設計コンパイラ (Meaning Design Compiler)

### 3.1 責務

人間向け資料を入力に取り、AI-facing layer の **候補** を生成し、人間が決めないと進めない点を **診断** として返す。

- 要求仕様の曖昧さ検出
- Capability 境界の推定と衝突検出
- Rule 候補 / UseCase 候補 / Decision ownership 候補の抽出
- failure reason / precondition の整合検査
- 三層化すべき箇所の提案 (`11-three-layer-disambiguation.md`)
- `MUST_DECIDE_AND_DOCUMENT` 候補の抽出 (`12-must-decide-and-document.md`)
- 人間向け診断メッセージの生成

> [!IMPORTANT]
> **コンパイラは「よしなに補完」しない。** 分からない点は勝手に決めず **止まる**。
> AI が断定で埋めると、それは「人間が所有すべき意味の決定を AI が奪う」ことになり、本方法論の芯に反する。

### 3.2 診断契約 — error / warning / info

| 重大度 | 意味 | 進行可否 |
| --- | --- | --- |
| **ERROR** | 人間が決めないと進めない (意味の欠落・矛盾) | **ブロック** (実装フェーズへ進めない) |
| **WARNING** | AI が決められるが **記録必須** (= `MUST_DECIDE_AND_DOCUMENT` 候補) | 進行可だが追跡 |
| **INFO** | 明確に抽出できた (確定) | 進行可 |

診断例 (議論で得た形):

```text
ERROR RUL-003: Rule "ManualCropOverridesAutoCrop" の適用者が未定義です。
  ImageVariant は値を保持すると書かれていますが、Rendering が優先関係を適用するかが明記されていません。

ERROR DEC-002: UC-07 SwapPlacements で異なる Grid 間の swap が許可されるか未定義です。

WARNING MD-004: "Repository not found" の表現が未指定です。AI 実装時の MUST_DECIDE_AND_DOCUMENT 候補です。
```

### 3.3 診断カタログ = findings ledger の順方向適用

コンパイラの検査ルールは **発明しない。`91-findings-ledger.md` から収穫する**。過去 finding はすべて「執筆時に検出できず後段で発覚した compile error」だからである。

| 過去 finding (由来) | コンパイラが authoring 時に出すべき診断 |
| --- | --- |
| `A-1` NotFound 失敗理由欠落 | `ERROR`: precondition 宣言に対応する canonical_failure_reason がない |
| `B-D3` cross-grid swap 未定義 | `ERROR`: UC-07 の境界条件 (同一 grid 制約) が未定義 |
| `F-1` / `F-2` 失敗理由の到達性 | `WARNING`: 自己検証 VO により到達不能な失敗理由 / `guaranteed_by` 未注記 |
| `D-1` / `I-C3a/b/c` applies_to drift | `ERROR`: `applies_to` と per-UC `failure_reasons` の不一致 (= 照合 C3 を前倒し) |
| `MD-1〜25` MUST_DECIDE | `WARNING`: AI 実装者が勝手に決めそうな点。MUST_DECIDE 候補 |
| `E-comp` 横断規約衝突 | `ERROR`: 契約 (21) に無い物理表現が必要 |

> [!IMPORTANT]
> これにより **PoC の実証資産がそのまま spec になる**。新規に検査基準を捏造するのではなく、
> 「実際に穴になった事例」を authoring 時に前倒しで弾く。② の照合ゲート (C1/C2/C3) と同じ検査を、
> コードが無い段階で BOM スキーマだけに対して回せる範囲は回す。

### 3.4 source map — provenance であり PLM トレーサビリティの中核

生成 BOM の各項目には **根拠 (source map)** を持たせる:

- この Rule は要求仕様のどの一文から来たか
- この UseCase はどのシナリオから抽出されたか
- この failure reason はどの失敗条件から来たか
- この Decision ownership は人間資料のどこで根拠づけられているか

| 効果 | 説明 |
| --- | --- |
| **断定の禁止** | 根拠が無い項目は AI が断定できない (§3.5 の provenance に直結) |
| **PLM 保守** | 要求が変わったとき、source map から **「どの BOM 項目を再訪すべきか」が機械的に辿れる**。BOM を「台帳」たらしめるのは source map |
| **逆監査** | 事後監査 (③) で観測 BOM と入力 BOM の差を、根拠単位で突き合わせられる |

### 3.5 provenance タグとゲート — 「AI は意味を所有しない」を構造で強制

生成 BOM 項目に provenance を付け、ゲート条件にする:

| provenance | 意味 | 実装フェーズへ進めるか |
| --- | --- | --- |
| `human-confirmed` | 人間が承認 (source map に根拠あり) | ○ |
| `info` (抽出確定) | コンパイラが明確に抽出 | ○ |
| `proposal` / `inferred` | AI が推定 (要 人間承認) | △ (承認待ち) |
| `unresolved` / `error` | 未決・矛盾 | **× (ブロック)** |

> `error` 級と `unresolved` が残る限り実装フェーズに進めない。これが「意味の最終決定権は人間」をツールで実装した形である。
> 手戻りは **コード修正ではなく BOM 改訂として蓄積** され、PLM 台帳に履歴が残る。

### 3.6 実装形態 — 決定的ツール + AI ハイブリッド (採用)

コンパイラは **AI 単体** でも **決定的ツール単体** でもなく、両者の **ハイブリッド** で構成する。
役割分界は「自然言語理解は AI、検証ゲートは決定的ツール」とし、**ブロックする ERROR は必ず決定的ツールが出す**。

| パート | 担当 | 入力 | 役割 | 出力の性質 |
| --- | --- | --- | --- | --- |
| **AI 抽出器** | AI | 人間向け資料 (文章) | NL → 構造化候補へ lift。Capability/Rule/UC/Decision 候補、source map、UI アーキタイプ照合、`MUST_DECIDE` 候補、三層化提案、**失敗理由を canonical 名へ正規化** (prototype C-1 の教訓) | **provenance タグ付き候補** (`inferred`/`proposal`)。自身を `human-confirmed` に昇格できない |
| **決定的検査器** | ツール (PyYAML 等) | 構造化 BOM (YAML) | スキーマ検証 + 横断整合検査。再現可能 | **ブロックする ERROR** (再現性あり、hallucination 不可) |

> [!IMPORTANT]
> **ブロックする ERROR は決定的ツールが出す** (AI ではない)。理由: ERROR は進行を止める判定なので、
> **再現可能で hallucination しないこと** が必須。これは Addendum J で受け入れゲートを AI 判断ではなく
> `checker.py` (決定的) に置いたのと同じ原則。AI は意味抽出と **advisory な WARNING/proposal** に徹し、
> 最終的な「進める/止める」の硬いゲートは決定的ツールが握る = 「AI は意味を所有しない」をツールで担保。

**決定的検査器 = ② 照合ゲート (`checker.py`) の static 部分集合を、BOM 単体入力で回したもの。** 新規発明ではなく既存ツールの延長:

| 検査 | ② 生成ゲート | ① authoring (本書) |
| --- | --- | --- |
| **C3** static cross-reference (`applies_to` ↔ per-UC `failure_reasons`) | 実装済 | **そのまま BOM 単体で回せる** |
| precondition 宣言 ↔ canonical_failure_reason の存在 | — | **新規 static ルール** (`A-1`/`B-D3` を authoring 時に弾く) |
| スキーマ/必須セクション/命名規約/forward-ref/数値-項目数整合 | 一部 | **新規 static ルール** (`14`/`Dpc-5` を機械化) |
| **PROV** provenance ゲート (AI の `unresolved`/`proposal` タグを block) | — | **新規 static ルール** — 意味的ギャップを AI タグ経由で機械 block (§3.7 の橋。prototype で実証) |
| **C1/C2** dynamic (失敗理由の到達性 / precondition 強制) | 実装済 (実コード probe) | **不可** (コード未生成) → ② に残す |

> dynamic 検査 (C1/C2) は実コードが要るので ② に残り、static 検査だけが ① に前倒しできる。
> 同じ穴を **① (BOM 段) と ② (実装段) の両方で別の手段が捕捉する** = §2 の shift-left 連続体の実体。

### 3.7 構造に還元できない意味的 ERROR の扱い — proposal-ERROR (決定)

決定的ツールが構造ルールで拾えない意味的 ERROR (例 `RUL-003` Rule 適用者未定義、`DEC-002` cross-grid swap 未定義) は、
**AI が `proposal-ERROR` として出し、人間確認で硬化 (harden) させる**。

`proposal-ERROR` = 重大度 **ERROR** (進行ブロック) × provenance **`proposal`** (要人間承認) の組み合わせ。
決定的ツール由来の ERROR と同じく **進行を止める** が、硬化されるまでは AI の提案にとどまる。

| 硬化の経路 | 人間の動き | 結果 |
| --- | --- | --- |
| **人間が回答できる** | そのまま **人間向け資料 (Human-facing layer) に記述** | 再 compile で `human-confirmed` に昇格 → ブロック解除 |
| **分からない** | **AI に相談して決定** (AI は consultant、最終決定は人間) | 決定内容を人間向け資料に記述 → 再 compile で昇格 |

> [!IMPORTANT]
> **この判断は人間に委ねる。** AI は意味的 ERROR を **提案** できるが、硬化 (= 進めてよいかの最終決定) は人間が握る。
> AI は相談相手 (consultant) であって **意味の所有者ではない**。これは §0「意味の最終決定権は人間」/ §5.2「AI が所有してはいけないもの」と一致する。
> 提案を ERROR 級で出す (補完して進めない) ことで、§3.2 の「よしなに補完せず止まる」契約も保たれる。

これにより診断の出所は 2 系統に整理される: **決定的ツール = 構造で拾える ERROR (再現可能)** / **AI = 意味的 proposal-ERROR + WARNING/INFO (advisory)**。
いずれも「人間が硬化するまで実装フェーズに進めない」(§3.5) という単一ゲートに収束する。

**実装機構 (prototype で実証、2026-05-30)**: proposal-ERROR は「AI が provenance を `proposal`/`unresolved` でタグ → 決定的ツールが **provenance ゲート (PROV)** でそれを機械的に block」として実装する。
**決定的ツールは意味的ギャップを理解しなくてよい。AI が付けたタグを enforce するだけ**で、構造に現れない意図の欠落 (cross-grid swap / decision ownership 欠落など) も再現可能に block できる。
これが「**検出は AI / enforcement は決定的ツール**」という分界点を渡す橋であり、§3.6「ブロックは決定的ツール (再現可能)」と本節「AI は所有しない」を同時に満たす (実測詳細: `../../experiments/authoring-compiler-prototype/RESULTS.md`)。

---

## 4. UI 意味契約層 (UI Semantic Contract)

UI も **見た目のレイアウト** と **意味的に必要な affordance** を分ける。`canonical_failure_reasons` の UI 版に相当する。

| 区分 | 誰が決めるか | コンパイルエラーになるか |
| --- | --- | --- |
| **Visual Layout** (配置・余白・色・サイズ) | **人間 (自由)** | ならない |
| **Interaction Contract** (何を入力でき、何を実行できるか) | 意味契約 | **欠落で ERROR** |
| **UseCase Binding** (操作がどの UseCase に対応するか) | 意味契約 | **欠落で ERROR** |
| **Feedback Contract** (成功/失敗/検証エラーの返し方) | 意味契約 | **欠落で ERROR** |
| **State Contract** (disabled / loading / error / authenticated) | 意味契約 | 欠落で WARNING〜ERROR |

**画面種別アーキタイプ**は最小契約を持つ。コンパイラはこれをテンプレートとして保持し、人間資料が契約を満たすか検査する:

| 画面種別 | 最小 interaction / usecase-binding (例) |
| --- | --- |
| login | ユーザー識別子入力 / パスワード入力 (秘匿表示) / ログイン実行 / 中止 (キャンセル・戻る) / 認証失敗フィードバック |
| search | 検索条件入力 / 検索実行 / 結果0件のフィードバック / クリア |
| edit | 編集対象ロード / 保存 / 破棄 / 検証エラー表示 / 未保存状態の警告 |
| list | 一覧表示 / 選択 / 空状態 / ページング or 全件方針 |
| confirm | 肯定 / 否定 / 破壊的操作の明示 |

診断例:

```text
ERROR UI-LOGIN-001: 「ログイン画面」と宣言されていますが、パスワード入力が定義されていません。
ERROR UI-LOGIN-002: ログインを中止する操作が未定義です。キャンセル/戻る/閉じる いずれかの意味を明記してください。
```

> UI の見た目は自由。しかし **UseCase を成立させる意味的部品とフィードバックは必須**。欠ければコンパイルエラー。

---

## 5. ワークフロー & 役割分担

### 5.1 工程表 (主担当 / 支援 / ツール / 成果物・Gate)

| 工程 | 主担当 | 支援 | ツール | 成果物 / Gate |
| --- | --- | --- | --- | --- |
| 1. 人間向け要求を書く | **人間主** | AI 補助 | Human Requirements Template *(新)* | 要求 / シナリオ / 用語 / 画面意図 |
| 2. 意味設計へ compile | **AI主** | 人間確認 | Meaning Design Compiler *(新)* | Capability / Rule / Decision 候補 + 診断 |
| 3. コンパイルエラー修正 | **人間主** | AI 診断 | Diagnostic Reporter *(新)* | 欠落・矛盾を **人間資料側で** 修正 → 再 compile |
| 4. AI向け BOM 生成 | **AI主** | 人間承認 | BOM Compiler *(新)* | Capability BOM / Rule Ledger / Decision Ownership (+ source map) |
| 5. UI 意味契約チェック | **AI主** | 人間判断 | UI Semantic Contract Checker *(新)* | 必須入力・操作・フィードバックの欠落検出 |
| 6. 実装プロンプト生成 | **AI主** | 人間承認 | Implementation Prompt Generator *(新, 09/12/40 ベース)* | AI-facing prompt / MUST_DECIDE list |
| 7. AI 実装 | **AI主** | 人間は原則コード非介入 | Implementation Agent | 生成コード / テスト / 実装ノート |
| 8. 照合ゲート | **ツール主** | 人間確認 | BOM Conformance Checker *(実証済: `22` + `checker.py`)* | **GATE: PASS / FAIL** |
| 9. BOM視点レビュー | **人間主** | AI 監査 | Capability BOM Auditor *(実証済: `01-10` / `09`)* | unclear / overreach / findings |
| 10. PLM 保守 | **人間主** | AI 補助 | Findings Ledger *(`91`)* / 契約版 (`21`) / source map | BOM 改訂履歴 / 契約バージョン / 根拠追跡 |

工程 1〜6 が本書で新たに体系化する Authoring 層 (うち多くがコンパイラ系の新ツール)。7〜10 は既存 PoC で実証済み。

### 5.2 責任の所在 (誤解防止の核)

| 区分 | 内容 |
| --- | --- |
| **人間が責任を持つ** | 何を作りたいか / どの意味を守るか / どの判断をどの Capability が所有するか / UI で意味的に必須な操作 / コンパイルエラーへの意味上の回答 / BOM・契約・findings の長期保守 |
| **AI が責任を持つ** | 人間資料からの候補抽出 / 欠落・矛盾の診断 / AI-facing 形式への変換 / 実装 / テスト生成 / 照合実行 / 事後監査レポート |
| **AI が所有してはいけない** | 業務意味の最終決定 / Rule の追加・改名 / Decision ownership の変更 / Capability 境界の変更 / UI 必須操作の省略 / 横断規約の独自解釈 |

> [!IMPORTANT]
> AI が立ち止まるべき条件 (= ① で `ERROR`/`unresolved` を出して人間に返す条件):
> BOM に矛盾 / Rule と failure reason が対応しない / Decision ownership 未定義 / 実装上どうしても決める必要がある /
> 横断規約 (21) に無い物理表現が必要 / 照合ゲートが落ちる / unclear が実装継続に影響する。
> このとき AI は補完して進まず、**人間が BOM 側を修正し、再度 compile/実装させる**。手戻りは BOM 改訂として蓄積される。

---

## 6. 既存 01〜22 との接続 (ギャップ表)

| 概念 | 既存 | 本書が足すもの |
| --- | --- | --- |
| 人間向け資料テンプレ | — | **新規** (要求/シナリオ/用語/画面意図、文章中心) |
| 執筆チェック | `14` (受動・AI-facing 成果物が対象) | **能動コンパイラ** (人間資料を入力、診断を出力) |
| 物理規約 | `21` 横断規約契約 | コンパイラが「契約に無い物理表現が要る」を `ERROR` 化 |
| 生成後照合 | `22` + `checker.py` | その **authoring 前倒し版** (① ↔ ② の連続体) |
| MUST_DECIDE | `12` | コンパイラが候補を自動抽出 (`WARNING`) |
| 三層化 | `11` | コンパイラが三層化すべき箇所を提案 |
| 事後監査 | `01〜10` / `09` | 工程 9 として運用モデルに位置づけ |
| **UI 意味契約** | — | **新規層** (§4) |
| **ワークフロー / 役割分担** | `14 §1` に執筆順のみ | **全工程 × 主従 × ツール × Gate** (§5) |

矛盾なく **上に乗る**。既存ドキュメントの大半は変更不要で、本層は外側のオーケストレーションを与える。

---

## 7. ロードマップと基盤決定 (Step 0 baseline 固定、2026-05-30)

実装順序は **「スキーマ/規約 (契約) を先に固定 → 抽出器と決定的検査器を固定基盤上で → 多 Capability 拡張と本体昇格は最後」** とする (Addendum J の churn 回避と同型)。

### 7.1 Sequencing plan (手戻り最小の順序)

| 順 | 作業 | 防ぐ手戻り |
| --- | --- | --- |
| **0** | 基盤固定 (本節 §7.2) | スキーマ/規約 churn が下流全部を巻き込むのを防ぐ |
| 1 | 抽出器の契約 (正規化 §7.2-a / 推定積極性 / provenance 規律) | §7.3 の推定方針と正規化を束ねて一度で |
| 2 | 決定的ルールの整理 (registry を canonical 名のみへ) | T2 完了後なので薄く済む (先にやると T2 で前提が変わり再調整) |
| 3 | UI トラック (アーキタイプ辞書 → 決定的ルール + 抽出器照合) | UI スキーマ予約 (§7.2-d) 済みで schema churn なし。1-2 と並行可 |
| 4 | 複数 Capability authoring 検査 | 純粋な消費者。単一が安定後でないと作り直し (E→F→G→H と同型) |
| 5 | 本体 01-10 への昇格 + 再番号 | churning draft を昇格しない (J の教訓) |

> **2 つの罠**: ① 正規化 (抽出器) と registry の family 化 は **競合する代替案** → 先に正規化を正と決め family 化を捨てる (§7.2-a)。② UI のスキーマ追加を後づけにすると抽出器/検査器が churn → スキーマ予約だけ Step 0 に繰り上げ (§7.2-d)。

### 7.2 Step 0 で確定した基盤 (fixed baseline)

以後の Step 1〜4 の前提。変更は §6 (UI なら §4) と整合させ、churn を避けるため安易に動かさない。

**(a) 失敗理由名の正規化は AI 抽出器の責務 (T2 採用 / T1 却下)**
AI 抽出器が失敗理由を **canonical 名へ正規化**する (`GridNotFound` → `NotFound` + `entity_kind` payload)。決定的 registry (`PRECOND_REASON_MAP`) は **canonical 名のみ**を持つ単純な状態に保つ。registry を family-aware にする案 (T1) は **却下**: 決定的ツールに意味的同値知識を埋めるのは §3.6 の分業 (決定的=構文/単純) に逆行する。
→ prototype の「命名相違 9 ERROR」は正規化導入で消える。`checker.py` 側の変更はほぼ不要 (registry は既に canonical 名のみ)。

**(b) canonical 語彙の置き場所 + 診断 ID 体系**
- canonical 失敗理由語彙: 各 Capability の `canonical_failure_reasons` が権威。横断で再発する語 (`NotFound`/`OutOfBounds`/`Conflict`/`InvalidDimensions` 等) は **baseline 語彙**として一度定義し継承 (規範継承 13 + 契約 21 の延長)。抽出器はこの語彙へ正規化する。
- 診断 ID 体系: BOM 要素種別に紐づく **安定した接頭辞**で固定 (findings ledger ID と同じく addressable に):

  | 接頭辞 | 対象 |
  | --- | --- |
  | `RUL-` | Rule (適用者未定義 / 曖昧 / 三層候補) |
  | `FAIL-` | 失敗理由 (欠落 / 命名 / payload) |
  | `PRE-` | precondition (被覆なし / 未定義) |
  | `DEC-` | Decision ownership (未定義 / 跨ぎ) |
  | `BND-` | 境界 / 共有概念 |
  | `UI-` | UI 意味契約 (interaction / binding / feedback / state) |
  | `MD-` | MUST_DECIDE 候補 |
  | `EVT-` | event / 観測可能性 |
  | `AC-` | 受け入れ条件のテスト可能性 |

  各診断は `<接頭辞-連番>` + severity (proposal-ERROR/WARNING/INFO) + source-map を持つ。

**(c) 二層境界 = free prose 入力 (prototype で実証)**
人間向け層の入力は **自由文 (prose)**。prototype が free prose → BOM の lift を実証したのでこれを baseline とする。任意で軽量テンプレ (背景/シナリオ/操作/用語/受け入れ条件 — prototype の INPUT が既に持つ構成) を **ガイドとして**示すが、**構造化は強制しない** (構造化は抽出器の仕事)。

**(d) BOM スキーマに `ui_contracts` セクションを予約 (判断のみ、実装は Step 3)**
UI 意味契約 (§4) 用のセクションを **今スキーマに予約**する (後づけ schema churn 回避 = 罠②)。最小形: 画面アーキタイプごとに必須 Interaction / UseCaseBinding / Feedback / State 契約。今は **場所と最小形だけ**確定し、アーキタイプ辞書の中身と決定的ルールは Step 3 で詰める。`checker.py` は未知セクションを無視する (`_find_key`) ため、予約による既存検査への影響はゼロ。

**deferrals (今やらない)**:
- **番号体系 (旧 §7-5)**: 23 → 24/25 分割は **昇格時 (Step 5) まで延期**。今分割すると相互参照が churn する。README 昇格ポリシーが「再番号は昇格時でよい」と明記済み。
- **本体 01-10 への昇格**: Authoring 層が Step 1〜4 で育つため最後 (Step 5)。

### 7.3 残る未確定 (downstream で詰める)

| 項目 | 詰める Step | 状態 |
| --- | --- | --- |
| 推定 (`inferred`) の積極性・severity 方針 | Step 1 | ✅ **完了** — `../tools/authoring-compiler/extractor-spec.md` RULE B (成否/境界/不変条件/所有権を左右する未定義は proposal-ERROR) + RULE C (provenance↔診断 severity 結合、prototype 不整合の修正) |
| UI アーキタイプ辞書の具体 (種類・精度・各契約の必須項目) | Step 3 | 🟡 |
| 複数 Capability の authoring 検査の具体 (共有概念 / 境界参照の前倒し検査) | Step 4 | 🟡 |

---

## 8. 実証 (prototype 実測済み、2026-05-30)

本方法論の流儀「実コードで実証する」(Addendum A〜I) に従い、採用形態 (b) を 2 段 prototype で実測した
(`../../experiments/authoring-compiler-prototype/`: 入力 prose / AI 出力 BOM+診断 / 拡張 checker / `RESULTS.md`)。

- **AI 抽出器** (独立 subagent、穴を非開示) が縮小版 GRID の人間 prose から BOM を lift + 診断を発行: **proposal-ERROR 12 / WARNING 13 / INFO 4**。
- **決定的検査器** (`checker.py --authoring`) が static 検査 (SCHEMA/C3/PRECOND/REF/PROV) を実行: GATE FAIL。正準 GRID BOM は PASS (区別できる)。

**実測された分界点**:

| 検出対象 | 捕捉者 | 根拠 |
| --- | --- | --- |
| 意図の不完全性 (重なり定義の曖昧さ / 順番値未定義 / UI フィードバック欠落 / cross-grid swap) | **AI のみ** | 内部整合な BOM は「未完成」でも構文的に正しく、構造に現れない |
| 内部不整合 / 規約準拠 / 相互参照 | **決定的ツール** | 本実験では AI が良く lift し C3/SCHEMA/REF clean。決定的 ERROR は PRECOND の **命名相違**のみ (= 規約準拠を見る) |
| 上記いずれの意味的ギャップも、AI が `unresolved`/`proposal` でタグ付け済みなら | **決定的ツール (PROV)** が機械 block | 橋 (§3.7)。ツールはギャップを理解せずタグを enforce |

**実証されたこと**: (1) (b) は authoring 時に機能する。(2) AI は 7 仕込み穴すべてを検出し、旧 ledger の実バグ `A-3` (Swap 自身排除) を prose から**独立に再発見** (SWAP-002)。(3) **検出は AI / enforcement は決定的ツール**、を provenance タグが繋ぐ。

**calibration 発見** (RESULTS §5): C-1 PRECOND の命名感受性 (→ AI が canonical 名へ正規化、§3.6 に反映済み) / C-2 SCHEMA の存在チェックだけでは不十分 (→ PROV が補完) / C-3 provenance が分界点を渡る橋 (→ §3.7 に反映済み)。

**残課題** (順序は §7.1): 正規化を抽出器責務化 (§7.2-a。T1=registry family 化は却下) → UI 意味契約ルールの決定的化 (Step 3) → 複数 Capability での authoring 検査 (Step 4)。

---

## 9. 関連ドキュメント

- 受動チェックリスト (本書の前身): `14-author-checklist.md`
- 三層構造 / MUST_DECIDE / 規範継承: `11` / `12` / `13`
- 横断規約契約 (物理表現の権威): `21-codebase-convention-contract.md`
- 意味設計コンパイラ (① の実体): `../tools/authoring-compiler/` (前段 `extractor-spec.md` + 後段 `checker.py --authoring`)
- BOM↔実装 機械照合 (② の実体): `22-bom-conformance-check.md` + `../tools/bom-conformance-check/checker.py`
- 診断カタログの源泉: `../evaluation/91-findings-ledger.md`
- 実証根拠 (PoC 全行程): `../evaluation/90-feasibility-notes.md` (Addendum A〜J)
