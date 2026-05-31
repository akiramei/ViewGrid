# パターン2 ガバナンス pilot — maintenance-task-1 結果

> 実施 2026-05-31。対象 `GRID_COMPOSITION` / 実 ViewGrid (.NET/C#)。
> 検証する問い: **as-built BOM は、AI 保守の逸脱 (壊してはいけない意味境界の破壊) を検出させられるか**。

## ループ構成 (AI が AI を監査する実運用形)
1. **as-built BOM v0.1** (`../GRID_COMPOSITION.as-built.v0.1.yaml`) = 監査基準 (意味地図)。
2. **独立 maintainer** (BOM 非開示): 「`SwapPlacementsUseCase` を可読性+重複削減でリファクタ」を依頼 → `candidate-A` (= `.after.cs`)。実 src は不触。
3. **planted deviation** (執筆者が SEEDED): stage-3 (swap 後の a/b 相互占有重複チェック) を「1/2 で十分」と誤判断して削除した over-simplify 版 → `candidate-B` (= `.deviation.cs`)。
4. **独立 auditor** (BOM を渡し 09 規範、A/B どちらが clean/broken か非開示): 各候補を before と BOM に照らし観測。
5. **執筆者が独立検証**: diff を自分で精査し auditor の判定を裏取り。

## 結果 — 両方向で成功
| 候補 | 実体 | auditor 判定 | 執筆者の ground truth | 一致 |
| --- | --- | --- | --- | --- |
| candidate-A | careful refactor (stage-3 を `Overlaps` ヘルパへ等価移設) | **PASS** (意味境界保存) | PASS (引数順・LINQ 化とも意味的に同一) | ✅ |
| candidate-B | stage-3 削除 (2 段検証へ退行) | **FAIL** (AR-02 fragile 違反) | FAIL (相互重複が素通り) | ✅ |

- **検出**: auditor は candidate-B の stage-3 欠落を **AR-02 `fragile`** 違反として捕捉し、**具体バグ入力を独立に構築** (a=1×1@(0,0), b=2×1@(1,0) を swap → セル (1,0) で重複。検証1/2 は `others` が a/b 自身を除外するため原理的に捕捉不可)。BOM の AR-02 as_built 注記「標準の候補 vs 既存検査では (3) を捕捉できない」が auditor を直接武装した。
- **sign-off (偽陽性なし)**: 大幅に再構築された candidate-A を「意味境界保存」と正しく認定。スタイル変化で cry-wolf しなかった。
- **副次**: auditor は doc コメントの意味縮小 (「互いに重複しない」→「他配置と重複しない」) まで捕捉 = コメント上の意図侵食も検出。

**結論: as-built BOM は、AI 保守者が善意の「簡素化」で fragile な不変条件 (旧 ledger A-3 と同型の、テストでしか出ない配置破壊) を落とすのを、独立 AI 監査者に検出させられる。同時に正当なリファクタを誤検出しない。Pattern 2 (保守ガバナンス) の中核仮説を実コードで実証。**

## BOM のどこが効いたか (価値の所在)
- `rules` の **`fragile: true`** フラグ (AR-02 stage-3 / AR-07 undo) — 監査の注意を集中させた。
- `audit_focus` の具体質問 (「swap 3 段検証 stage-3 削っていないか」) — チェックリストとして直接機能。
- `as_built_divergences` / AR-02 の「盲点」注記 — なぜ検証1/2 では足りないかを auditor に与えた。
→ 価値は §rules.fragile + §audit_focus + as-built 注記に集中。理想化 sample BOM だけでは届かない「現実の壊れやすさ」を地図化したことが効いた。

## meta-findings (方法論側へ戻す)
- **F-P1 (✅ 実証)**: ガバナンスループ (BOM + 独立 maintainer + 独立 auditor) は fragile-invariant 逸脱に対し機能。検出と sign-off の両方向。
- **F-P2 (limitation / 次ラウンド)**: 今回の逸脱は audit_focus に **明示列挙済み** の項目だった。BOM の汎化力 (列挙外の逸脱を捕捉できるか) は未検証 → 次は audit_focus に無い逸脱 (例: AR-05 PixelOffset の境界チェック追加 = 仕様違反、または undo 対称性 AR-07 の破壊) を植えて試す。
- **F-P3 (reproducibility → 次の改善対象)**: 監査は AI 判断 (非決定的)。別 run で見落とすリスク。AR-02 stage-3 は **「実在し・catchable」と実証された破壊パターン** = ユーザーが defer した **C# dynamic conformance harness / anchor test の最初の落とし込み先**として最有力。「破壊パターンを観測してから harness に落とす」(ユーザー方針) の観測が完了した。
- **F-P4 (positive)**: BOM 駆動監査はコメント上の意味縮小も捕捉 = コード差分だけでなく宣言/意図の侵食も拾う。

## pilot スコープ (この round でやらないこと、再掲)
ViewGrid 全体 BOM 化 / 全再生成 / UI 網羅監査 / EF 完全一致は対象外。次の自然な一手は F-P2 (列挙外逸脱) か F-P3 (AR-02 を決定的 anchor test 化)。
