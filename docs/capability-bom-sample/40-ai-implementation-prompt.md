# 40 — AI 実装プロンプト雛形 (GRID_COMPOSITION)

> **Version: v0.2** (v0.1 からの主な変更: `MUST_DECIDE_AND_DOCUMENT` を第三カテゴリとして明示 / Anchor tests への参照を追加)

## このドキュメントの位置づけ

本書は、`GRID_COMPOSITION` Capability を AI に実装させる際の **プロンプト雛形** である。

Capability BOM Audit における AI プロンプトの 8 構造
(Goal / Scope / Non-goals / Capability context / Allowed / Forbidden / Output format / Confidence policy)
を **実装フェーズ用に転用** したもの。

> [!IMPORTANT]
> 通常の Capability BOM Audit プロンプトは「コードを書かせない」が原則だった。
> 本書はその **逆方向の利用** であり、AI に **コードを書かせる** ことを目的とする。
> ただし「意思決定の所在」「Rule の保証場所」「Capability 境界」は依然として AI に変更を許さない。

---

## A. 完全版プロンプト (コピー & ペースト用)

> 以下のブロックは、AI に渡すプロンプトの完成形である。
> 必要に応じて `{...}` 部分を埋めて使う。

```text
あなたは Capability BOM Audit 方法論に従って動作するソフトウェア実装者である。
本タスクで実装する Capability は GRID_COMPOSITION のみである。

== INPUT DOCUMENTS ==
以下のドキュメントを正準入力として扱うこと。

1. docs/capability-bom-sample/10-requirements.md
   (要求仕様 — 何を作るか)

2. docs/capability-bom-sample/20-capability-bom.md
   (Capability BOM — 意思決定の所在と境界)

3. docs/capability-bom-sample/21-grid-composition.yaml
   (機械可読 BOM — 上記の正準データ。Markdown と矛盾する場合は YAML が正)

4. docs/capability-bom-sample/30-design.md
   (設計書 — Rule ledger・Entity 意味・Decision spec・Persistence 境界)

ドキュメント間で矛盾を見つけた場合は、実装を進める前に質問として明示せよ
(unclear / suspected / partially_verified を使い、推測で進めないこと)。

== GOAL ==
GRID_COMPOSITION の全 UseCase (UC-01 〜 UC-11) を実装し、
全 Rule (R-01 〜 R-09) を指定された保証場所で保証し、
全 Event を指定された発行タイミングで発行する。

成功条件:
- 30-design.md §6.1 の「必須テストカテゴリ」が網羅されている
- すべての Rule が「30-design.md §1 Rule Ledger」で宣言された場所で唯一保証されている
- Decision ownership 表 (20-capability-bom.md §6) に違反する実装がない

== SCOPE ==
- 対象 Capability: GRID_COMPOSITION のみ
- 他 Capability (IMAGE_VARIANT_MANAGEMENT, HISTORY_MANAGEMENT, RENDERING_EXPORT) は
  最小限のスタブ / インターフェースとして表現してよい
- ワークスペース管理・UI レイアウト・PNG 出力は対象外

== NON-GOALS ==
以下を行ってはならない:

- ViewGrid の既存実装を参照すること
  (元実装に引きずられないため。参考にするのは本サンプル成果物のみ)
- Capability の範囲を超える機能を追加すること
  (例: ImageCopy の編集機能を含めない)
- Rule の名前・ID を変更すること
- UseCase の失敗理由名を変更すること (失敗理由の追加も禁止)
- Decision ownership 表に違反する設計をすること
  (例: UI 層が R-01 / R-02 を判定するコードを書く)
- DB / Repository に Rule 保証を委ねること
  (例: DB ユニーク制約で R-06 を保証する)
- 「綺麗そう」「SOLID 違反」「責務過多」を理由に Decision の所在を勝手に動かすこと

== CAPABILITY CONTEXT ==
GRID_COMPOSITION が解くべき問題:
グリッド (N 行 × M 列のセル格子) 上に画像派生物 (ImageCopy) を配置し、
境界と非重複を保証しつつ編成する。

本 Capability は配置の妥当性に関する唯一の権威である。
本 Capability は ImageCopy の意味解釈をしない (CopyId の存在性のみ問う)。

== ALLOWED (AI が自由に決めてよい、報告不要) ==
- プログラミング言語
- フレームワーク (UI / ORM / テスト)
- クラス・モジュール分割
- ファイル配置・命名 (用語集に従う限り)
- DI コンテナ使用の有無
- イミュータブル / ミュータブル実装スタイル
- イベント発行機構 (in-process pub/sub, message bus 等)
- ロギング・テレメトリ
- パフォーマンス最適化 (要求 §4.5 の目安を満たす限り)

== MUST_DECIDE_AND_DOCUMENT (AI が決めてよいが実装ノートに明示する義務がある) ==
v0.2 で導入された第三カテゴリ。サンプル文書から一意に決まらないが、
実装上は決定せざるを得ない事項。決定内容を IMPLEMENTATION_NOTES.md に列挙すること。

代表例:
- Timestamp の時間帯 (UTC か local か)
- Repository の "not found" 表現 (None / Optional / 例外)
- トランザクション境界の実装機構 (DB トランザクション / メモリスナップショット 等)
- イベントバスの同期性 (sync / async)
- 失敗理由の Exception 階層設計 (BaseException 継承の有無、payload の型)
- Enum vs 文字列定数 (Axis, OrderOperation など)
- 言語選択の根拠 (なぜその言語か)

これらは ALLOWED と異なり「書類に書く義務」がある。なぜなら:
- 後続の監査 / 改訂で「なぜこう決まったか」を追跡可能にする
- 同じサンプル文書で異なる AI が異なる決定をした場合の **横断比較** を可能にする

== FORBIDDEN (AI が変更してはならない) ==
- Rule ID / 名称 (R-01 〜 R-09)
- UseCase ID / 名称 / 失敗理由名 (UC-01 〜 UC-11、`canonical_failure_reasons` セクション参照)
- Event 名 / 発行タイミング
- Capability 境界 (20-capability-bom.md §8)
- Decision ownership 表 (20-capability-bom.md §6)
- 用語集の語の意味 (10-requirements.md §5)
- Anchor tests (30-design.md §8 AT-01 〜 AT-10) の **期待振る舞い**
  - テスト関数の実装スタイルは ALLOWED だが、期待値の変更は禁止

== OUTPUT FORMAT ==
以下を含む実装一式を生成すること:

1. ソースコード
   - Domain Model
   - UseCase (UC-01 〜 UC-11)
   - Repository インターフェース (実装はインメモリのスタブで可)
   - Event 発行機構 (テスト可能な形)

2. テストコード
   - 30-design.md §6.1 の必須テストカテゴリを網羅
   - **30-design.md §8 の Anchor tests AT-01 〜 AT-10 をすべて実装**
     (テスト関数名から検索可能な形にすること: 例 `test_at_01_*`)
   - **30-design.md §6.3 の Property-based test (random walk) を必須として実装** (v0.2 で必須化)
   - R-01 〜 R-09 を独立にテスト
   - 各 UC の happy path と failure path

3. 実装ノート (実装の意味的メモ、ファイル名は IMPLEMENTATION_NOTES.md)
   - Decision ownership 表に対する自己監査結果
     (どのクラス / 関数が、どの Role / Decision を持っているかを記述)
   - 残存する unclear / suspected_overreach 箇所
   - **MUST_DECIDE_AND_DOCUMENT 項目の列挙 (最低 5 件)** ── 上記 ALLOWED と FORBIDDEN
     のどちらでもない、サンプル文書が一意に決めていない決定事項とその選択理由
   - Anchor tests の合格状況 (AT-01 〜 AT-10 すべてパスしたか)

4. README
   - ビルド方法
   - テスト実行方法
   - 言語・フレームワークの選定理由 (1 段落)

== CONFIDENCE POLICY ==
- 入力ドキュメントから一意に決まらない事項は、推測で進めず "unclear" として残す
- Rule の保証場所が複数候補ある場合 (まれ) は "suspected_overreach" として両方を記述
- 仕様の歪み・矛盾を発見した場合は実装を止め、質問として明示する
- 「断定強制」「全部を綺麗にする」方向の最適化はしない

== POST-IMPLEMENTATION SELF-AUDIT ==
実装完了後、自身の生成物に対して以下を実施:

1. 各 Rule (R-01 〜 R-09) について、保証コードが 1 箇所に存在するか確認
   - 複数箇所に分散している場合は実装ノートで "suspected_overreach" として記録
   - **例外**: UC-07 の post-swap intersection check は R-02 ロジックの 2 箇所目では
     なく UC-07 workflow_decision として位置づける (30-design.md §1 R-02 参照)

2. 各 UseCase について、入力 → 結果が単一関数として表現可能か確認
   (副作用が混入していないか)

3. Event 発行が状態変更と独立にテスト可能か確認

4. UI 層 (存在する場合) が以下を持っていないか確認:
   - owns
   - enforces

5. **Anchor tests (AT-01 〜 AT-10) がすべてパスするか確認**
   - パスしないものがあれば実装ノートに理由を明記
   - 「曖昧で実装できない」場合はテストを変更せず unclear として残す

6. **MUST_DECIDE_AND_DOCUMENT 項目を最低 5 件、実装ノートに列挙したか確認**

これら 6 つの自己監査結果を実装ノートに記載すること。
```

---

## B. プロンプト各要素の解説 (人間レビュー用)

### B.1 INPUT DOCUMENTS 指定の意義

AI は **本サンプル外のドキュメントを参照しない** ことを明示的に要求している。
ViewGrid の既存実装をコピーして提出するのを防ぐため。

### B.2 YAML が正準である理由

20-capability-bom.md (Markdown) は人間向け、21-grid-composition.yaml は機械向け。
両者で矛盾が生じた場合の **解決ルール** を明示しておかないと AI は迷う。
本書は「YAML が正」と固定しているが、運用上は **Markdown と YAML を同時に編集** することが推奨される。

### B.3 NON-GOALS の各項目の動機

| Non-goal | 動機 |
| --- | --- |
| 既存実装を参照しない | PoC として「白紙からの生成」を観測する |
| 機能を追加しない | Capability スコープを越える AI の暴走を防ぐ |
| Rule 名を変更しない | 監査時の追跡可能性のため |
| 失敗理由名を変更しない | UI / 上位層との契約を固定 |
| Decision ownership を動かさない | 方法論の核心違反 |
| Rule 保証を Repository に委ねない | Decision の権威が永続化に流出 |
| 「綺麗そう」で Decision を動かさない | 09-ai-audit-prompt-guide.md の中心警告 |

### B.4 FORBIDDEN リストが「ID / 名称」に集中する理由

実装方針 (クラス分割・パターン適用) は AI 任意でよい。
固定すべきは **「他のドキュメント・コードから参照される識別子」** のみ。
これにより:

- 実装の自由度を最大化しつつ
- 監査時に Rule / UseCase / Event を **コード横断で grep 可能** な状態を保つ

### B.5 CONFIDENCE POLICY で "unclear" を許す理由

`09-ai-audit-prompt-guide.md` の中心原則:

> AI は断定を強制されると過剰推論しやすい

このため、不明な点は **明示的に質問として残す** ことを許容する。
むしろ「全部勝手に決められた」結果は本方法論にとっての失敗である
(人間の意思決定の所在が見えなくなる)。

### B.6 POST-IMPLEMENTATION SELF-AUDIT の意義

実装後、AI 自身に **逆方向の Capability BOM Audit** を実行させる。
この自己監査の結果が、人間レビュー時の最初の入口になる。

---

## C. プロンプト使用上の注意

### C.1 プロンプトサイズと前提

このプロンプト本体は数百行程度。AI モデルの context window が
ドキュメント全体 (`10-` 〜 `30-` + YAML) を保持できることが前提。

context が不足するモデルの場合は、

1. プロンプト本体 (本書 §A)
2. 21-grid-composition.yaml (機械可読、最も圧縮されている)
3. 30-design.md §1 Rule Ledger
4. 30-design.md §6 テスト戦略

の優先順位で渡すことを推奨。

### C.2 言語指定の有無

本プロンプトは言語を指定していない。
評価目的によっては言語を指定する変種を作るとよい:

- **「ユースケースを満たすか」の評価**: 言語不問 (本プロンプトのまま)
- **「同じ技術スタックで再実装可能か」の評価**: 「C# / .NET 10 で実装すること」を ALLOWED に追加し FORBIDDEN は変えない

### C.3 自己監査の信頼性

AI の自己監査は不完全であることに注意。
最終評価は **別の AI セッション** または **人間** が独立に行うこと。
Capability BOM Audit Phase 3 (事後監査) で別 AI に逆向き観測させるのが推奨。

---

## D. プロンプトの変種

### D.1 部分実装プロンプト

UC-01 と UC-05 のみ実装させる場合、SCOPE 部分を:

```text
SCOPE:
  - GRID_COMPOSITION の UC-01 (CreateGridCanvas) と UC-05 (PlaceImageCopy) のみ
  - 他の UseCase はインターフェースのみ宣言し、本体は未実装で可
  - Rule R-01, R-02, R-03, R-04, R-05, R-06 は完全実装
```

に置き換える。Capability 全体を AI が一気に書くのが現実的でないモデルへの対応。

### D.2 検証専用プロンプト

実装は別途用意し、本プロンプトの仕様に **適合しているか** を AI に検証させる場合は
Goal を:

```text
GOAL:
  既存の実装が、本書で定義された GRID_COMPOSITION の Rule / UseCase / Event /
  Decision ownership に適合しているかを観測する。

  コード修正は禁止。観測のみ行う。

  出力形式は 09-ai-audit-prompt-guide.md 推奨形式に従う。
```

に置き換える。これは **通常の Capability BOM Audit** に戻る。

---

## E. 関連ドキュメント

- `~/OneDrive/ドキュメント/Capability BOM Audit/09-ai-audit-prompt-guide.md` — プロンプト設計の原典
- `10-requirements.md`, `20-capability-bom.md`, `21-grid-composition.yaml`, `30-design.md` — 本プロンプトの入力
- `90-feasibility-notes.md` — このプロンプトを使った試行の評価メモ
