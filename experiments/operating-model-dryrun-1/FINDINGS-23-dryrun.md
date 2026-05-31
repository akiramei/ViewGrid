# methodology/23 「実運用で枯らし」findings ledger

> 目的: `methodology/23-authoring-and-operating-model.md` (draft) を実運用に乗せて穴を出し尽くし、
> draft としての不完全性・実装とのドリフト・未実装工程を炙り出して堅牢化する (loop-until-dry)。
> 実施: 2026-05-31。baseline 確認: checker の 4 実証 (good GRID PASS / prototype v1 FAIL12 / authoring-set XSHARED2 PASS / dynamic C2 D-3 FAIL) が**今も記述どおり生存**。

各 finding: `状態` ✅修正済 / 📝記録のみ / 🟡オープン。

---

## Round 1 — 静的ドリフト / 内部整合スイープ (執筆者直)

23 を実装 (checker.py / extractor-spec)・git 状態・samples と照合。**共通の根: blueprint が「実装済みで実証された部分」と「notional/計画の部分」を明示的に区別していない** → 読者が「全工程にツールがある」と誤読しうる。「枯らす」= この境界を明示する。

### R1-a — 進捗記述が Step 5 完了前で凍結 (stale progress) 🟡→R3で修正
- **場所**: §7.1 進捗 (「Step 4 … 完了。残るは Step 5」)、§7.3 表、§8 残課題 (「Step 0〜4 完了。残るは Step 5 のみ」)。
- **実態**: Step 5 の**昇格は完了** (commit `4a1108b`、拡張 11-14/21/22 を canonical へ保守的昇格)。**再番号は意図的に延期** (churn 回避)。23 自身は frontier ゆえ「churning draft は昇格しない」ポリシーで **draft 据え置き**。
- **修正方針**: 進捗を「Step 5 昇格完了 / 再番号延期 / 23 は frontier ゆえ draft 維持」へ更新。

### R1-b — §3.6 検査表が未実装の static ルールを実装済みのように併記 🟡→R3
- **場所**: §3.6 表の行「スキーマ/必須セクション/**命名規約**/forward-ref/**数値-項目数整合**」を「新規 static ルール (`14`/`Dpc-5` を機械化)」と記載。
- **実態**: checker.py `check_schema` は**必須セクション/フィールドの存在**と、REF (forward-ref) のみ実装。**命名規約 (naming regex) と 数値-項目数整合 (Dpc-5) は未実装**。
- **修正方針**: 実装済み (SCHEMA存在/C3/PRECOND/REF/UI/PROV) と 未実装 (命名規約・Dpc-5) を表で明示分離。

### R1-c — §5.1 工程表が「新ツール」の実体を過大表示 (最大の finding) 🟡→R3
- **場所**: §5.1 工程表。工程 1-6 に「新ツール」を割当てるが実体が揃っていない:
  | 工程 | §5.1 のツール | 実体 |
  | --- | --- | --- |
  | 1 人間向け要求 | Human Requirements Template (新) | **実体ファイル無し** (§7.2-c は free prose + 任意の軽量テンプレを「ガイド」と言うのみ) |
  | 2 意味設計へ compile | Meaning Design Compiler (新) | **実体あり** = extractor-spec + `checker.py --authoring` |
  | 3 エラー修正 | Diagnostic Reporter (新) | コンパイラの診断出力に**統合** (独立ツール無し) |
  | 4 AI向け BOM 生成 | BOM Compiler (新) | 工程2 と**同一コンパイラ**の再 run (R1-d) |
  | 5 UI 意味契約チェック | UI Semantic Contract Checker (新) | **実体あり** = `checker.py check_ui_contracts` (コンパイラに統合) |
  | 6 実装プロンプト生成 | Implementation Prompt Generator (新) | **実体無し** (samples/prompts は手書き 41/42/43、generator は不在) |
- **核心**: 6 つの「新ツール」のうち実体は実質 **1 つのコンパイラ** (= checker --authoring、工程2/4/5 を兼ねる)。工程1/6 は未実装、工程3/4 はコンパイラに統合。
- **修正方針**: §5.1 に **実装状況マーカー** (実装あり / コンパイラに統合 / 未実装) を付し、運用モデルを「どこにツールがあり、どこが手作業/未実装か」で正直にする。

### R1-d — 工程2 と 工程4 は別ツールではなく同一コンパイラの前後 run 🟡→R3
- **場所**: §5.1 が Meaning Design Compiler (工程2) と BOM Compiler (工程4) を別ツールとして列挙。
- **実態**: prototype は単一 extractor pass で BOM 候補+診断を生成。工程4 = 工程3 (人間が prose 修正) 後の**同一コンパイラ再 run** で clean BOM を得る、が正しい読み。別ツールではない。
- **修正方針**: 「工程2/4 は同一コンパイラを工程3 を挟んで 2 回 run (compile→診断→prose修正→再compile)」と明記。

### R1-e — UI の AI→archetype lift は本ドライランまで end-to-end 未実証 (pending R2)
- **場所**: §4.2 / §8 「Step 3 完了、H7 を [UI][ERROR] で決定的に捕捉 (smoke-test)」。
- **実態**: その smoke-test は **hand-built login BOM** に対する決定的側の検証のみ。RULE E (AI 抽出器が prose から archetype を認識し `ui_contracts` へ lift) の **end-to-end は未実行**。R2 が初の通し実証。
- **状態**: R2 出力で確定。

### R1-f — 診断接頭辞 / decision taxonomy / archetype の セキュリティ・ライフサイクル被覆 (pending R2)
- **仮説**: §7.2-b 接頭辞 (RUL/FAIL/PRE/DEC/BND/UI/MD/EVT/AC) と decision 7 種 (domain/validation/workflow/persistence/ui_interaction/rendering/history) は、**情報開示・本人確認のようなセキュリティ判断**や **session lifecycle** を綺麗に収められるか? login archetype は `auth_failure` を持つが **locked_out** フィードバックを持たない。
- **状態**: R2 (アカウントアクセス・ドメイン) 出力で確認 → decision taxonomy / archetype library のドメイン被覆ギャップなら methodology 本体への finding 候補。

---

## Round 2 — 新ドメイン実運用ドライラン (独立 subagent)

題材: `INPUT-prose.md` (アカウントアクセス: ログイン/パスワード変更、UI 2 画面)。穴: `SEEDED-HOLES.md` S1-S9。
独立 subagent (2 ファイルのみ読了を自己申告 + 出力が裏取り) が `OUTPUT-bom.yaml` / `OUTPUT-diagnostics.md` を生成。
執筆者が `checker.py --authoring` を独立に実行: **GATE FAIL / 14 blocking ERROR / 0 INCONCLUSIVE / exit 1**。

### 生実測
- AI 抽出: 2 UC / 6 Rule / 6 canonical_failure_reasons / UI 2 画面。診断 proposal-ERROR 14 / WARNING 7 / INFO 2。
- 決定的検査 14 ERROR の内訳: PRECOND 1 (UC-02 Authenticated 被覆無し) / UI 3 (Login=cancel欠落, ChangePassword=load/save/discard + validation_error/unsaved_warning欠落) / PROV 10 (AI が proposal/unresolved タグ済みの意味的ギャップ)。

### 検出採点 (S1-S9): 全件捕捉 ✅
S1 情報開示=FAIL-001 (user enumeration 明示) / S2 ロック時間=RUL-002,FAIL-003 / S3 カウント単位=RUL-001,DEC-003 / **S4 login cancel 欠落=決定的 [UI][ERROR]** / S5 変更画面 archetype=edit 認識+決定的照合 / S6 最小長=FAIL-002,DEC-002 / S7 セッション寿命=BND-002 / S8 退会粒度=AccountWithdrawn 独立 (resolved) / **S9 本人確認=PRE-001+RUL-003+DEC-001+決定的 PRECOND ERROR**。
emergent: BND-001 (リセットフロー皆無), EVT-001 (ロックは失敗観測が前提), EVT-002 (監査ログ), PRE-002 (メール正規化が成否に効く), DEC-004 (保存方式 unresolved), AC-001 (しきい値未数値化でテスト不能)。

### positive (主張の確証)
- **F-R2-A (R1-e 確証)**: **UI seam を初めて end-to-end 実証**。AI が prose→archetype 認識→`ui_contracts` lift → 決定的 `check_ui_contracts` が AI 産出の契約に対し `[UI][ERROR]` 発火 (login cancel)。従来は hand-built BOM の smoke-test のみ。→ §4.2/§8 を「end-to-end 実証済」に更新可。
- **capability 非依存性の確証**: 画像ドメイン外で spec がクリーンに機能。RULE A 正規化 `UserNotFound→NotFound` が画像外でも作動。
- **F-R2-F (ゲート信号純度)**: 命名ノイズ 0・INCONCLUSIVE 0。14 ERROR のうち ~12 が純粋な意味的ギャップ。残り 2 は F-R2-B 由来の archetype 不適合ノイズ (要改善点)。

### 新規 finding (枯らしの核心)
- **F-R2-B 🟡 (archetype ライブラリのギャップ + UI ロール正規化欠落 — 最重要)**:
  - (B1) `edit` archetype は `load`/`discard`/`unsaved_warning` を要求するが、パスワード**設定**画面には意味的に不適合 (既存値を load/表示しない、unsaved 警告も曖昧)。AI は最も近い `edit` を選び、決定的ツールが load/discard/unsaved_warning を要求 → **真の authoring ギャップでなく archetype 不適合由来の ERROR**。ライブラリに「form / set-value / credential 入力」系 archetype が不足 (§4.1)。
  - (B2) 主たる確定操作のロール名が archetype 間で不統一 (login=`submit` / edit=`save` / confirm=`affirm`)。AI は「保存ボタン」を `submit` と lift → edit の `save` 必須と不一致で決定的が `save` 欠落を誤検出。**UI トラックに RULE A 相当の interaction ロール正規化が無い** (保存/ログイン/送信/確定/OK → canonical primary role の対応表が欠落)。
  - 影響: ゲート信号純度を 2/14 だけ汚す (F-R2-F)。→ §4.1 ライブラリ拡張 (set 系 archetype) + RULE E にロール正規化表、を methodology 候補として記録。
- **F-R2-C 🟡 (RULE E が semantic outcome から UI feedback を過剰 lift)**: AI が「失敗したら入れない」(操作の**結果**) から `auth_failure` **feedback affordance** を lift。RULE E は「prose が実際に述べた affordance だけ」だが、結果セマンティクス (→ failure_reasons) と「画面が失敗を表示する」(→ feedback affordance) は別層。これにより seeded の「失敗表示欠落」が決定的 [UI][ERROR] で出るはずが AI の lift で**マスク**された (UI-001 WARNING で内容未定義は拾ったが構造 ERROR は消えた)。prototype の C-1 と同型の calibration finding。→ extractor-spec RULE E を鋭くする (結果が起きうる=failure_reasons / 画面が結果を表示=feedback、後者は prose が表示に言及した時のみ lift)。✅ R3 で spec 修正。
- **F-R2-D 🟡 (decision taxonomy がセキュリティ判断を被覆しない)**: 情報開示 (user enumeration)・認可/本人性 (誰がパスワード変更可)・資格情報保存 (ハッシュ) は 7 種 (domain/validation/workflow/persistence/ui_interaction/rendering/history) に第一級の居場所が無い。AI は散らして対処 (情報開示→FAIL/UI 診断のみ=**所有者が記録されない** / 認可→validation_decision+Rule / 保存→persistence unresolved)。最もセキュリティ critical な「どちらが間違いか漏らすか」の判断が decision_ownership に**所有者なし**で落ちた。→ methodology 本体 04 への finding 候補 (`security_decision`/`authorization_decision` の追加、or 23 に「セキュリティ判断は DEC/FAIL/UI 診断で表面化するが専用 ownership 種別が無い」と明記)。新ドメインが taxonomy の CRUD/画像偏重を露出。
- **F-R2-E ✅ (接頭辞は十分 — R1-f 仮説の一部反証)**: §7.2-b 接頭辞 (FAIL/PRE/RUL/DEC/EVT/BND/UI/AC/MD) は全論点を収容。lifecycle は BND/EVT で表現でき**新接頭辞は不要**。R1-f の「接頭辞ギャップ」仮説は反証。真のギャップは decision taxonomy (F-R2-D) と archetype library (F-R2-B) であって診断 ID 接頭辞ではない、と sharpen された。

---

## Round 3 — 23 への反映と収束判定 (執筆者直)

### 23 への反映 (commit 候補、未コミット)
| finding | 反映先 | 種別 |
| --- | --- | --- |
| R1-a | §0 枯らし状況 / §7.1 進捗 / §8 残課題 — 「Step 0〜5 完了 (`4a1108b`)、再番号延期、23 は frontier ゆえ draft 据置」に更新 | ✅ ドリフト修正 |
| R1-b | §3.6 検査表 — 実装済 (SCHEMA/C3/PRECOND/REF/UI/PROV) と 未実装 (命名規約/Dpc-5) を行分離 + UI 行追加 | ✅ ドリフト修正 |
| R1-c/d | §5.1 工程表 — ツール列を実装状況マーカーへ + IMPORTANT 注記 (実体は実質 1 コンパイラ / 工程2-4-5 同一 / 工程1-6 未実装 / 工程7-10 実証済) | ✅ 過大表示是正 |
| F-R2-A | §4.2 — UI seam を「hand-built smoke-test → AI lift に対する end-to-end 実証」に更新 | ✅ positive 反映 |
| F-R2-B | §4.1 NOTE — set 系 archetype 不足 + 主操作ロール正規化欠落 (RULE A 相当の UI 版) を未解決 finding として明記 | 📝 記録 + extractor-spec RULE E に部分対応追加 |
| F-R2-C | extractor-spec RULE E — feedback と結果セマンティクスを分離 (prose が表示に言及した時のみ feedback lift) | ✅ spec 修正 (マスク機構はコード上自明で確認、AI 再 run 検証は次サイクル) |
| F-R2-D | §5.2 NOTE — decision taxonomy のセキュリティ判断被覆ギャップ。本体 `04` 候補 | 📝 記録 (本体昇格サイクルの設計判断) |
| 実証根拠 | §8.1 dry-run 2 節を新設 (positive + 新規 finding + 収束) | ✅ 追加 |

post-edit 回帰スモーク: good GRID `PASS` / dry-run `FAIL` / cross-cap `PASS` (全維持、編集は docs のみで checker 不変)。

### 収束判定 (loop-until-dry) — **収束**
- R1 (blueprint 実装状況の明示) は全件 23 内で解消。R2 finding は 2 クラスタ (blueprint honesty / UI・taxonomy のドメイン被覆) に**きれいに収束**し、新たな失敗様式の連鎖は無し。
- R2 は 2 大主張 (capability 非依存 / UI seam end-to-end) を**確証** = positive 収束。
- 残るオープン (F-R2-B archetype library / F-R2-D decision taxonomy) は **methodology 本体 (04 / §4.1) の設計判断**であって 23-internal の churn ではない。「churning draft は昇格しない」ポリシー上、これらは将来の本体昇格サイクルへ引き渡す。
- **結論**: 23 の blueprint としての枯らしは R1+R2 で収束。23 は内部整合・実装状況の demarcation・実証根拠 (end-to-end) を備えた。さらなる枯らしは methodology 本体 (taxonomy/archetype) を対象とする別ワークストリーム。3 ラウンド目 (無 UI/多 Capability ドメイン) は限界収量が低いと判断し回さない。
