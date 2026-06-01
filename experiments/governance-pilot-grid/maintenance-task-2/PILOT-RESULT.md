# パターン2 ガバナンス pilot — maintenance-task-2 結果 (F-P2: BOM の汎化力)

> 実施 2026-06-01。対象 `GRID_COMPOSITION` / 実 ViewGrid (.NET/C#)。
> 検証する問い: **as-built BOM は、audit_focus に列挙されていない逸脱を、独立 AI 監査者が意味境界から*推論して*捕捉できるか** (= BOM は「意味地図」か、それとも「チェックリスト」にすぎないか)。

## 背景 — F-P1 との差 / なぜ F-P2 が必要か
F-P1 (maintenance-task-1) で検出した逸脱 (swap stage-3 削除) は **audit_focus に 1:1 で明示列挙済み** の項目だった。よって「BOM が効いた」のか「チェックリストに当たっただけ」なのかが分離できていなかった。F-P2 は **audit_focus のどの質問にも名指しされていない逸脱** を植え、auditor が rules / owns / does_not_own / decision_ownership / boundaries / divergences の**意味宣言から推論**して捕捉できるかを試す。

## ループ構成
1. **基準** = as-built BOM 全文 (`../GRID_COMPOSITION.as-built.v0.1.yaml`)。
2. 架空の保守タスク「**placement 系 UseCase を改善して**」に対し AI 保守者が作ったとする **3 つの独立候補** (`*.before.cs` / `*.candidate-{A,B,C}.cs`)。
3. **独立 auditor** (別 AI、本会話の文脈を持たない fresh subagent)。渡したのは BOM 全文 + 09 監査規範のパスのみ。**どれが正/壊か、audit_focus やルール ID への誘導は一切与えない**。09 規範に従い「観測のみ・evidence-first・unclear 許容・修正禁止」。
4. **執筆者 (本セッション) が独立に ground truth を保持**し、auditor 判定と照合。

### 候補 (中立ラベル A/B/C、非列挙性が肝)
| 候補 | 対象 | 実体 | 非列挙性 |
| --- | --- | --- | --- |
| A | MovePlacementUseCase | 可読性リファクタ (descriptor 構築 / Error 変換をヘルパ抽出、意味保存) | 偽陽性対照 |
| B | MovePlacementUseCase | `PlacementValidator.Validate` から **`excludePlacementId` を削除** (「冗長として整理」) | audit_focus に「自己除外」を名指す項目なし |
| C | PlaceImageCopyUseCase | 初期 OccupySize を `copy.OccupySize` 継承でなく **画像のアスペクト比から自動算出** (`IImageAssetRepository` を新規注入し Asset.Size を解釈) | audit_focus に「画像形状→占有」を名指す項目なし |

## 結果 — 3/3 正答・偽陽性なし
| 候補 | 実体 | auditor 判定 | ground truth | 一致 | auditor が引いた BOM 箇所 |
| --- | --- | --- | --- | --- | --- |
| A | 正当リファクタ | **PASS** | PASS | ✅ | `excludePlacementId` / 同位置 no-op (AR-06) / 検証単一源 / in-place 更新の保存を確認、validator 引数 before 完全同一 |
| B | AR-02 自己除外削除 | **FAIL** | FAIL | ✅ | **AR-02 (fragile)「対象自身は除外」** + 具体シナリオ + **AR-07 / D-5 への連鎖** |
| C | 画像解釈で越境 | **FAIL** | FAIL | ✅ | **does_not_own「バリアントの意味解釈/画像同一性」** + `decision_ownership.image_identity` + `boundaries.depends_on` の `read_only`/`existence_check_only` 契約 + **D-1** |

### auditor が独立構築した具体シナリオ (evidence-first)
- **候補B**: 2×2 占有の placement P を (0,0) から (0,1) へ 1 セル移動。自己除外が無いと P 自身の旧占有が `existing` に残り、移動先と旧占有が交差 → **自分自身との重複で誤って Conflict 拒否**。「隣接セルへ少し動かす」操作 (占有が前後で重なる限り) が常に失敗する。
- **候補C**: 横長画像 (1920×1080) のコピーを 3 列グリッドに配置。before は `copy.OccupySize`=1×1 で成功するが、candidate-C は横長判定で 2×1 を自動採用 → 右端列で OutOfBounds、隣接が埋まっていれば Conflict。**配置妥当性の結果そのものが画像形状に依存**して変わる (「妥当性の唯一の権威」が画像意味解釈に汚染)。

## 汎化の証拠 (= F-P2 の核心)
1. **B も C も audit_focus の 11 質問に 1:1 で名指しされていない**。にもかかわらず auditor は `rules`(AR-02 の自己除外節)・`does_not_own`・`decision_ownership`・`boundaries`・`divergences`(D-1/D-5) から**推論して**両方を FAIL 判定した。
2. **auditor がチェックリスト不適用を自覚した上で意味推論した**: 候補B について「これは audit_focus[UseCase 迂回/検証スキップ] *ではなく*、検証の意味取り違え (自己重複を他配置重複と誤判定)」と明記。= チェックリスト照合ではなく意味地図からの推論。
3. **隣接チェックリスト項目に引きずられなかった**: 候補C は audit_focus 項目5 (「CopyId は存在確認のみで意味解釈していないか」) と主題が隣接するが、auditor はそれを引かず `does_not_own`/`decision_ownership`/`boundaries.depends_on` を引いた (より精密な越境宣言)。
4. **cross-cutting 推論のボーナス**: auditor は候補B が **AR-07 (undo 対称性) へ連鎖**する事を自力で辿った — `MovePlacementCommand.UndoAsync` が同じ Move UseCase を呼ぶため、戻し移動も自己重複で失敗し UndoRedoService がスタック全クリアしうる。これは **D-5 の「配置 UseCase を変えると undo が静かに壊れる」警告の実演**で、BOM の divergence 注記が単一 diff を超えた波及推論を可能にした。

## 結論
**as-built BOM は「意味地図」として汎化する。** 列挙外の逸脱 2 種 (ルールの自己除外節違反 / Capability 越境) を、独立 AI 監査者が宣言された意味境界から推論して捕捉し、かつ正当リファクタを誤検出しなかった。F-P2 (limitation: 列挙外逸脱で BOM の汎化力は未検証) を解消。BOM の価値は audit_focus チェックリスト**だけ**でなく、rules/owns/does_not_own/decision_ownership/boundaries/divergences の**意味宣言全体**にあると実証された。

## caveat / follow-up
- **非決定性 caveat**: 1 run・強力モデルによる結果。AI 監査は非決定的で、これは**証拠であって証明ではない**。robust 化の本命は依然 anchor test (F-P3/F-P5/F-P6) = catchable な不変条件を決定的 CI ガードへ落とす路線。F-P2 は「どの逸脱が catchable か」を**列挙外まで広げて観測**した段階に当たる。
- **follow-up (anchor 候補)**: 候補B は実バグでもある (N×M 配置の自己重複移動を誤拒否 + undo 連鎖破壊)。既存 Move テストがこの自己除外を被覆しているかは未確認 — F-P3/F-P5/F-P6 と同型の anchor test 化候補 (別ラウンド)。候補C は「越境を伴う新機能」であって既存改修でない点も、保守タスクの線引き材料として記録。

## スコープ (この round でやらないこと)
ViewGrid 全体 BOM 化 / 全再生成 / 複数 auditor run による統計化 / 候補B の anchor test 化は対象外 (上記 follow-up)。
