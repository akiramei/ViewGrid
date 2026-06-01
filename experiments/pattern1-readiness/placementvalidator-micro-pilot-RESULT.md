# PlacementValidator Micro-Pilot — 結果 (Pattern 1 の 2 例目、実証済)

> 実施 2026-06-01。crop resolver (F-P10/F-P12) に続く **2 例目**。
> 計画: `placementvalidator-micro-pilot-plan.md` / 凍結入力 spec: `placementvalidator-spec.md` / 生成物: `generated/PlacementValidator.gen.cs`。
> 問い: d-3 の `generation_overlay` が crop の (値変換+優先+丸め+VO method) を超え、
> **幾何制約・overlap/conflict・self-exclusion・判定順序・結果オブジェクト error channel・自己検証 VO 入力**へ汎化するか。

## 手順 (実施)
1. **oracle 硬化**: `PlacementValidatorTests.cs` に coverage_gaps を falsifier 化 (+4: 非正 grid→OutOfBounds / null existing→throw / exclude 後に別配置と衝突 / conflict Id = 反復順で先)。本番構成 (analyzers ON) で **PlacementValidatorTests 18 件 green** を確認 (= as-built 固定。conflict 同定の determinism は `as-built-incidental` とテストに明記)。
2. **enriched spec を凍結** (`placementvalidator-spec.md`): PV-1 overlay の behavior_contract (判定順序 / 上限のみ境界 / self-exclusion / conflict 同定=反復順で先) + error_channel (結果オブジェクト) + ctor_guard + vo_method_contract (OccupiedCells row-major)。**実装コード片なし**。
3. **blind 再生成**: 独立生成器に **spec のみ** を渡し (`src/`・`tests/` 非開示)、`PlacementValidator.cs` を生成。生成器は **tool use 0 回 = リポジトリ未参照** (真の blind を確認)。生成物 = `generated/PlacementValidator.gen.cs`。
4. **swap 検証**: 生成物を実 src へ一時上書き → 全テスト → `git checkout` で revert。

## 結果 — 生成物は意味等価 (oracle 範囲で drop-in 等価)。ただし生 artifact は analyzer gate で build 失敗
意味 (振る舞い) と style (記法) を **分離して** 検証した。

### (A) 意味等価 — analyzers/style/warnings off で全スイート pass
生 artifact は project の analyzer/style 契約に違反するため (下記 B)、**振る舞いの等価性だけを分離して観測する**ために `-p:RunAnalyzers=false -p:EnforceCodeStyleInBuild=false -p:TreatWarningsAsErrors=false` で build+test した:

| 検証 (生成物を src に swap) | 結果 |
| --- | --- |
| ViewGrid.Core.Tests | **186 pass / 0 fail** (PlacementValidatorTests 18 件 = 既存 7 + PV-2 硬化 4 を含む) |
| ViewGrid.Application.Tests | **466 pass / 1 skip / 0 fail** (下流の Move/Swap/Place/UpdateOccupySize 配線 + VM が *生成* 物に対して green) |

→ **spec のみから blind 生成したコードが、既存スイート全 652 件 (+1 skip) の意味等価判定を通過。** revert 後 src clean。crop と異なり **silent な数値発散は皆無** (整数セル演算=丸め無、conflict 同定/列挙順まで一致)。

### (B) style 差 — 本番構成 (analyzer gate ON) では生 artifact が build 失敗
生 artifact をそのまま (analyzers ON =実 CI 構成で) build すると、意味は正しいのに **3 つの style/analyzer 違反**で fail する:

| 違反 | 生成物 | 本番 (実装) | gate |
| --- | --- | --- | --- |
| **IDE0161** | block namespace `namespace ... { }` | file-scoped `namespace ...;` | `.editorconfig` `csharp_style_namespace_declarations = file_scoped:warning` → `TreatWarningsAsErrors` で error |
| **CA1510** | `throw new ArgumentNullException(nameof(existingPlacements))` | `ArgumentNullException.ThrowIfNull(existingPlacements)` | `AnalysisLevel = latest-recommended` |
| **IDE0005 / CS8019** | 冗長な file `using System.Linq;` (global using と重複・未使用) | global using (`Directory.Build.props`) のみ、file using なし | `ImplicitUsings` + `EnforceCodeStyleInBuild` |

## 生成物 vs 実装の意味比較
| 項目 | 実装 | 生成 | 等価性 |
| --- | --- | --- | --- |
| 判定順序 (null→throw > 非正grid→OOB > 上限境界→OOB > overlap→Conflict > Valid) | ✓ | ✓ | 完全一致 |
| 境界 = 上限のみ (`endX>gridCols||endY>gridRows`、下限は CellPosition VO が保証) | ✓ | ✓ | 完全一致 |
| self-exclusion (`excludePlacementId` は overlap 走査のみスキップ、境界に無影響) | ✓ (`is not null`) | ✓ (`HasValue`) | 完全一致 (記法差のみ) |
| conflict 同定 = `existingPlacements` 反復順で最初に重複した既存の Id | ✓ | ✓ | 完全一致 |
| OccupiedCells row-major (dy 外 / dx 内、`origin.X+dx, origin.Y+dy`) | ✓ | ✓ | 完全一致 |
| 結果チャネル = `PlacementValidationResult` (Valid/OutOfBounds/Conflict(Id))。例外でなく結果オブジェクト | ✓ | ✓ | 完全一致 |
| newCells 構築 | `.ToHashSet()` | `new HashSet<>(...)` | 意味一致 (記法差) |
| 前提ガード null→`ArgumentNullException` | `ThrowIfNull` | `throw new ...` | 意味一致 / **style 差 (CA1510)** |
| namespace / using | file-scoped / global のみ | block / 冗長 file using | 意味無関係 / **style 差 (IDE0161/IDE0005)** |
| **数値発散 (crop の丸めに相当)** | — | — | **無し** (整数セル=丸め非依存。crop の killer gap がここには存在しない) |

## ★ PV-3 gap feedback — crop と対照的な「2 層」の安全網
**最重要の対比**:
- crop (F-P10) の発散 (`ToPixelBbox` 丸めモード) は **silent** だった — oracle が中間値を踏まず green をすり抜けた。閉じるには `generation_overlay` への **意味次元補填**が必要だった (F-P12)。
- PlacementValidator の差 (file-scoped ns / CA1510 / 冗長 using) は **loud** — **analyzer gate が build 時に即座に捕捉**する。oracle カバレッジに依存せず、すり抜けない。

→ **意味は overlay (oracle 被覆) で収束させ、style は gate (analyzer/formatter) が担保する、という二層構造**が示唆される。具体的フィードバック:
1. **generation_overlay に「project code-style/analyzer 契約」次元を追加**: file-scoped namespace 必須 / `ArgumentNullException.ThrowIfNull` (CA1510) / global usings の列挙 (重複 file using 禁止) / `TreatWarningsAsErrors`・`AnalysisLevel`。spec が project の style/analyzer 契約を pin していなかった = **PV 版 gap** (crop の丸め gap に相当する構造)。
2. **または生成工程に gate を組み込む**: blind 生成物を `dotnet format` + analyzer build に通す step を含める。style は overlay で記述するより gate で機械的に正規化する方が確実 (意味と違い oracle 盲点がない)。
3. **示唆**: crop gap は「テストで踏まれない細部」(coverage 盲点) で overlay 補填が要る。validator gap は「analyzer が機械的に検出する記法」で gate が担保する。**両者は別種の安全網**であり、Pattern 1 を広げるには overlay (意味) と gate (style) の二層を併用すべき。

## メタ問い (この 2 例目で確認できたこと)
generation_overlay は crop の (値変換+優先+丸め+VO method) から validator の (幾何境界 + overlap/conflict + self-exclusion + 判定順序 + 結果オブジェクト error channel + 自己検証 VO 入力) へ **意味次元では汎化した** — blind 生成物は新次元 (判定順序 precedence / conflict 同定=反復順で先 / 結果オブジェクト / self-validating 入力) をすべて意味一致で再現。**残った gap は意味でなく style/analyzer 契約**であり、これは overlay の意味記述では拾えず gate が担う領域だった。

## 結論
- **Pattern 1 の再現性を 2 例目で確認**: crop に続き、適切に境界された oracle-backed な幾何コア (PlacementValidator) も、as-built spec から **意味等価 (既存スイート全通過) なコードを blind 再生成できた**。判定順序・self-exclusion・conflict 同定・row-major まで一致。
- **2 例目の新発見 = gap の種類が違う**: crop の本質 gap は *silent な数値発散* (overlay 補填が必要)。validator の gap は *loud な style/analyzer 違反* (gate が担保)。**意味=overlay、style=gate の二層**が Pattern 1 の安全網として要る、という構造が見えた。
- **caveat**: 「等価」は依然 *既存 oracle カバレッジの範囲内*。conflict 同定の `as-built-incidental` (反復順依存) は固定したが *意図された* 契約かは未決定 (決定点 D 候補)。全域 conformance には property テストが要る (defer 中の既知ギャップと整合)。

## 成果物 / 永続物
- `placementvalidator-micro-pilot-plan.md` (PV-0 調査 + PV-1 overlay 設計、凍結)。
- `placementvalidator-spec.md` (blind 生成の凍結入力、実装コード片なし)。
- `generated/PlacementValidator.gen.cs` (blind 生成物の記録、非コンパイル。tool use 0 回)。
- `tests/ViewGrid.Core.Tests/UseCases/PlacementValidatorTests.cs` の **+4 oracle 硬化** (永続。非正 grid / null existing / exclude 後別衝突 / conflict Id 反復順=as-built。実コードの coverage 改善でもある。Core.Tests 182→186)。
- 本 RESULT。実 src (`PlacementValidator.cs`) は変更なし (swap は検証後 revert)。

## next
- **PV-3 を d-3 スキーマへ反映**: `generation_overlay` に `code_style_contract` 欄 (file-scoped ns / CA ルール / global usings) を追加するか、生成工程に `dotnet format` + analyzer gate step を規約化するか (= 意味 overlay / style gate の二層を明文化)。
- conflict 同定の `as-built-incidental` を deliberate 化するか (人間決定 D 候補、保留)。
- 本線 (3) 工程管理層へ。crop の IO-1 是正 (commit 2bc4aa9) に続く 2 例目として、Pattern 1 の「BOM→blind 生成→収束」が幾何ルールでも再現することを確認した段階。
