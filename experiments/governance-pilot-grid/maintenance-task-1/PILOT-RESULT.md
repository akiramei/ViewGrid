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
- **F-P2 (✅ 実証済、2026-06-01 — `../maintenance-task-2/PILOT-RESULT.md`)**: 当初 limitation だった「BOM の汎化力 (audit_focus 列挙外の逸脱を捕捉できるか)」を検証 → **3/3 正答・偽陽性なし**。独立 auditor が、列挙外の 2 逸脱 (AR-02 の `excludePlacementId` 自己除外削除 / Place が画像アスペクトから占有を導出する Capability 越境) を、rules/does_not_own/decision_ownership/boundaries/divergences から**推論して**捕捉し、正当リファクタは PASS とした。**BOM は「チェックリスト」でなく「意味地図」として汎化する**ことを実証。caveat: 単 run・非決定的 = 証拠であって証明でない (robust 化の本命は anchor test 路線)。
- **F-P3 (reproducibility → 次の改善対象)**: 監査は AI 判断 (非決定的)。別 run で見落とすリスク。AR-02 stage-3 は **「実在し・catchable」と実証された破壊パターン** = ユーザーが defer した **C# dynamic conformance harness / anchor test の最初の落とし込み先**として最有力。「破壊パターンを観測してから harness に落とす」(ユーザー方針) の観測が完了した。
- **F-P4 (positive)**: BOM 駆動監査はコメント上の意味縮小も捕捉 = コード差分だけでなく宣言/意図の侵食も拾う。

## pilot スコープ (この round でやらないこと、再掲)
ViewGrid 全体 BOM 化 / 全再生成 / UI 網羅監査 / EF 完全一致は対象外。

---

## F-P3 — AR-02 stage-3 を決定的 anchor test 化 (2026-05-31、実施・実証済)

ガバナンスの価値連鎖を「AI 監査(非決定的)」から「決定的 CI ガード」へ落とし込んだ。

### 発見した盲点 (BOM 由来)
実 `tests/ViewGrid.Application.Tests/UseCases/SwapPlacementsUseCaseTests.cs` の既存 6 swap テストを精査 → **AR-02 stage-3 (a/b の *相互* 占有重複) を被覆するテストが無い**。`Returns_Conflict_When_Swap_NxM_Hits_Other_Placement` は第三の配置 (blocker) との衝突 (= 検証 1/2) を見ており、stage-3 ではない (テスト作者がコメントで衝突シナリオ構築に苦労した跡あり)。→ as-built BOM の `AR-02 fragile` 指摘が **実コードの欠落 anchor test を特定**。

### 追加した anchor test
`Returns_Conflict_When_Swap_Would_Make_AB_Mutually_Overlap` — a=1×1@(0,0) / b=2×1@(1,0)、swap で a→{(1,0)}・b→{(0,0),(1,0)} がセル (1,0) で相互重複 (第三配置なし)。`Conflict` を期待 + 拒否で両配置が元位置のままを確認。

### 決定的実証 (dotnet test)
| 対象 SwapPlacementsUseCase | 既存 6 件 | 新 anchor test |
| --- | --- | --- |
| **実コード** (stage-3 あり) | PASS | **PASS** (合計 7 pass) |
| **deviation** (stage-3 削除) | **全 PASS** | **FAIL** (失敗 1 / 合格 6) |

→ **既存スイートは stage-3 を未被覆 (deviation が 6 件素通り)。新 anchor test だけが決定的に捕捉。** deviation は一時差し込み後 `git checkout` で復元 (src clean)。

### 含意
- **価値連鎖が end-to-end で完成**: as-built BOM の `fragile` 指摘 → AI 監査が逸脱を catchable と確認 → 既存テストの盲点を特定 → 決定的 anchor test で恒久ガード化 (AI 判断不要・CI で再現可能)。
- 一般原則: **BOM の `rules.fragile` は「決定的 anchor test を持つべき不変条件」の優先リスト**になる。次の自然な展開は AR-07 (undo 対称性) 等、他の fragile に同手順を適用。
- F-P2 (列挙外逸脱で BOM 汎化力テスト) は **✅ 実証済** (`../maintenance-task-2/PILOT-RESULT.md`、3/3・偽陽性なし)。

---

## F-P5 — AR-07 undo 対称性 (配置層 OccupySize) を決定的 anchor test 化 (2026-06-01、実施・実証済)

F-P3 の手順 (BOM fragile → AI 監査が catchable 確認 → 既存テストの盲点特定 → 決定的 anchor test) を 2 つ目の fragile 不変条件 **AR-07 (undo/redo 対称性)** に適用した。

### 盲点の精査 (AR-07 を不変条件ごとに分解)
AR-07 は複合不変条件なので、列挙された sub-invariant を一つずつ既存テストに照合した:
| AR-07 sub-invariant | 既存テスト | 被覆 |
| --- | --- | --- |
| Place の Id 安定 (Redo は同 Id 再 INSERT) | `PlaceCommand_Execute_Undo_Redo_RoundTrip` (createdId で照合) | ✅ |
| Remove の PixelOffset 復元 | `RemovePlacementCommand_Restores_Full_State_Including_PixelOffset` | ✅ (※ PlacementOrder は未検証=残課題) |
| Move の before/after 対称 | `MovePlacementCommand_Reverts_To_Before_Position` | ✅ |
| Swap 冪等 (Undo=同じ Swap) | `SwapPlacementsCommand_Symmetric_Execute_Undo_Redo` | ✅ |
| **UpdateOffset の before/after 対称** | `UpdatePlacementOffsetCommand_RoundTrip` | ✅ |
| **UpdateOccupy の before/after 対称** | **— なし —** | **❌ 盲点** |
| UndoRedoService スタック整合 (依存破綻で全クリア) | `UndoRedoServiceTests` 多数 (RecordingCommand) | ✅ |

→ 配置系 6 Command のうち **`UpdatePlacementOccupySizeCommand` だけ round-trip テストが皆無**。兄弟の Offset Command には対称テストがあるのに、OccupySize には無い非対称な欠落。

### 盲点が「見えにくい」理由 (D-1 との接続)
`GridAndCopyCommandTests.UpdateImageCopyCommand_RoundTrip_Restores_All_Fields` に `OccupySize` の undo/redo 検証は**ある**。しかしそれは `_fx.CopyRepository.FindByIdAsync(copy.Id)` を見ており **ImageCopy(コピー層)の OccupySize** = 新規配置の初期値であって、**GridPlacement(配置層)の OccupySize ではない**。grep で "OccupySize" + "undo/redo" を走査すると hit するため、表面的なカバレッジ確認では「OccupySize undo は被覆済」と**誤読**する。これは BOM の **D-1 / audit_focus「OccupySize 二層」**(copy の OccupySize 変更を既存配置に波及すると誤解するな) が警告する取り違えと**同じ断層**で、GRID_COMPOSITION 自身の AR-07 不変条件が無防備だった。

### 追加した anchor test
`PlacementCommandTests.cs::UpdatePlacementOccupySizeCommand_RoundTrip` — 配置 (1×1@(0,0)) の OccupySize を 1×1→2×2 に拡張 (AR-04 で拡張は検証される)、Undo で 1×1 に縮小して戻る、Redo で再拡張。before/after 対称を直接確認。

### 決定的実証 (dotnet test)
| 対象 UpdatePlacementOccupySizeCommand | 既存 457 件 | 新 anchor test |
| --- | --- | --- |
| **実コード** (Undo=before) | PASS | **PASS** (合計 457 pass / 1 skip) |
| **deviation** (Undo=after 再適用 = 対称性破壊) | **全 PASS** (456) | **FAIL** |

→ **既存スイートは配置層 OccupySize undo を未被覆 (deviation が 456 件を全て素通り、コピー層 OccupySize undo テストも兄弟 Offset テストも green のまま)。新 anchor test だけが決定的に捕捉。** deviation は一時差し込み後 `git checkout` で復元 (src clean)。

**再現手順 (決定的)**: `src/ViewGrid.Application/History/Commands/UpdatePlacementOccupySizeCommand.cs` の `UndoAsync` を 1 行書き換える —
`=> _useCase.ExecuteAsync(_placementId, _before, ct);` を `=> _useCase.ExecuteAsync(_placementId, _after, ct);` に。
これで `dotnet test tests/ViewGrid.Application.Tests` は `UpdatePlacementOccupySizeCommand_RoundTrip` のみ FAIL・他 456 PASS / 1 skip。元に戻せば 457 PASS / 1 skip。(narrative でなく一行 diff で誰でも再現できる。)

### 含意 (F-P3 を補強)
- 価値連鎖が **2 つ目の fragile** でも再現。`rules.fragile` を anchor test の優先リストとして使う運用が反復可能と確認。
- 新知見: **複合 fragile 不変条件は sub-invariant に分解して照合せよ**。AR-07 は 7 つの sub-invariant のうち 6 つが被覆済で、欠けていた 1 つ (配置層 OccupySize) が**他層の同名テストに紛れて見えにくい**盲点だった。BOM の意味境界 (D-1「二層」) が、テストカバレッジの偽陽性 (層取り違え) を看破する物差しになった。
- 残課題: Remove の全 snapshot 復元のうち **PlacementOrder** は依然未検証 (z-order は D-8 で実質「作成順」=低リスクだが、厳密には AR-07 の盲点)。F-P-next 候補。 → **F-P6 (下記) で解消済**。

---

## F-P6 — AR-07 残盲点 (Remove の PlacementOrder 復元) を決定的 anchor test 化 (2026-06-01、実施・実証済)

F-P5 で残課題に挙げた「`RemovePlacementCommand` の全 snapshot 復元のうち **PlacementOrder**(重なり順)が未検証」を、同手順で anchor test 化した。AR-07 の sub-invariant 分解で唯一残っていた穴。

### 盲点 (F-P5 と同型: 隣接テストが本物の穴を覆い隠す)
- 既存 `RemovePlacementCommand_Restores_Full_State_Including_PixelOffset` は**名前に "Full State" と謳いながら PixelOffset しか検証していない**。PlacementOrder は無被覆。
- grep "PlacementOrder" すると hit はするが、それらは `PlaceImageCopyUseCaseTests` の**作成時採番 (1,2,3)** 検証と、renderer/fork の seed セットアップだけ。**削除→undo での復元を見ているテストは皆無**。表面走査では「PlacementOrder はテスト済」と誤読する (F-P5 の「OccupySize 二層」取り違えと同じ構図)。

### 追加した anchor test
`PlacementCommandTests.cs::RemovePlacementCommand_Restores_PlacementOrder` — A/B/C を順に配置 (order=1/2/3)、**中間の B (order=2)** を snapshot して削除→undo。復元された B の PlacementOrder が **2 そのまま**であること、かつ A=1 / C=3 が不変であることを確認。中間 order を狙うことで「最前面へ積み直し (=4)」「既定値 (0)」「再採番」のいずれの退行も決定的に弾く (RemovePlacementUseCase は再採番しない=D-8 を前提)。

### 決定的実証 (dotnet test)
| 対象 RemovePlacementCommand.UndoAsync | 既存 463 件 | 新 anchor test |
| --- | --- | --- |
| **実コード** (snapshot を AddAsync で完全復元) | PASS | **PASS** (合計 464 pass / 1 skip) |
| **deviation** (復元配置を最前面へ積み直す z-order 正規化) | **全 PASS** (463) | **FAIL** |

→ **既存スイートは PlacementOrder 復元を未被覆 (deviation が PixelOffset テスト含む 463 件を全て素通り)。新 anchor test だけが捕捉。** deviation は一時差し込み後 `git checkout` で復元 (src clean)。

**再現手順 (決定的)**: `src/ViewGrid.Application/History/Commands/RemovePlacementCommand.cs` の `UndoAsync` で `AddAsync(_snapshot, ...)` の直前に、`_snapshot.PlacementOrder` を「グリッド内 max+1 (空なら 1)」へ書き換える行を足す (= 復元を最前面へ正規化)。これで `RemovePlacementCommand_Restores_PlacementOrder` のみ FAIL・他 463 PASS / 1 skip。

### 含意
- **deviation が「typo」でなく意味的動機を持つ善意の変更**(「削除した配置を undo したら最前面に出てほしい」という妥当に見える UX 判断)である点が F-P3/F-P5 より一段強い。AR-07 の「undo は削除前の*正確な*状態を復元する」という不変条件が、もっともらしい正規化欲求を弾く境界として機能した。
- **AR-07 の sub-invariant 分解が完了**: 7 つ全てに決定的 anchor が付いた (BOM の `coverage_note` に明記)。GRID_COMPOSITION の 2 つの fragile rule (AR-02 / AR-07) は、宣言された不変条件が全て CI ガード化された状態に到達。
- 一般原則 (F-P5 を補強): **隣接する同名/類似テスト (PixelOffset 復元・作成時採番) が、本物の盲点 (削除→undo 復元) を「被覆済」に見せかける**。BOM の sub-invariant 列挙が、この種のカバレッジ偽陽性を機械的に炙り出す物差しになる。
