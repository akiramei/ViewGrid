# PlacementValidator Micro-Pilot — PV-0 調査 + PV-1 overlay 設計 (Pattern 1 の 2 例目)

> 実施 2026-06-01。crop resolver (F-P10/F-P12) に続く **2 例目**。
> 問い: d-3 の `generation_overlay` が crop の (値変換+優先+丸め+VO method) を超え、
> **幾何制約・overlap/conflict・self-exclusion・判定順序** にも汎化するか。
> 対象限定注入: `PlacementValidator.Validate` + `OccupiedCells` の純粋幾何コアに絞る (全配置系を曖昧再生成しない)。

## PV-0 現状調査
**対象 = `PlacementValidator` (src/ViewGrid.Core/UseCases/PlacementValidator.cs、純 static)** = 配置妥当性の唯一権威。
- public 表面: `Validate(OccupySize, CellPosition, int gridRows, int gridCols, IReadOnlyCollection<ExistingPlacement>, Guid? excludePlacementId=null) → PlacementValidationResult` / `OccupiedCells(CellPosition, OccupySize) → IEnumerable<CellPosition>` (row-major)。
- 型: `ExistingPlacement(Guid PlacementId, CellPosition Position, OccupySize OccupySize)` / `PlacementValidationResult`(IsValid + Reason + ConflictingPlacementId、static Valid/OutOfBounds/Conflict(Guid)) / `PlacementInvalidReason{None=0,OutOfBounds=1,Conflict=2}`。
- 判定順序: ①null existing→throw ②非正 grid→OutOfBounds ③上限境界 `endX>gridCols||endY>gridRows`→OutOfBounds ④重複走査(excludePlacementId スキップ、最初の重複→Conflict(thatId)) ⑤Valid。
- 呼び出し元: Place / Move(excludePlacementId=self-exclusion、F-P7) / Swap(stage1/2=Validate、stage3 相互重複は SwapPlacementsUseCase が OccupiedCells を合成=F-P3) / UpdateOccupySize / VM。

**crop との違い (= 2 例目の価値):**
| 次元 | crop resolver | PlacementValidator |
| --- | --- | --- |
| 中核 | precedence + null + rounding | 幾何境界 + overlap/conflict + self-exclusion + 判定順序 |
| 入力ドメイン | plain-data VO (無検証=呼出側責任) | self-validating VO (CellPosition 負値 throw / OccupySize >0 throw) で型が保証 |
| error channel | CropFraction? null | 結果オブジェクト PlacementValidationResult (IsValid+Reason+Id) |
| 数値 | midpoint 丸め (killer gap) | 整数セル=丸め無。代わりに列挙順 / conflict 同定順 |

**既存 oracle (PlacementValidatorTests、7):** 境界(両軸)/conflict(single/multi)/隣接非衝突/self-exclusion/OccupiedCells row-major。
**coverage_gaps:** ①非正 grid→OutOfBounds ②複数重複時の conflict Id 決定性 (反復順依存、未規定) ③null existing throw ④exclude 後に別配置と衝突 ⑤multi-cell conflict の Id。

## PV-1 generation_overlay 設計 (対象 = Validate + OccupiedCells)
```yaml
generation_overlay:
  target: PlacementValidator (Core/UseCases、純 static; Validate + OccupiedCells)
  generation_scope:
    generate: [ "Core/UseCases/PlacementValidator.cs (Validate + OccupiedCells + ExistingPlacement + PlacementValidationResult + PlacementInvalidReason)" ]
    given_types: [ OccupySize(自己検証), CellPosition(自己検証) ]
    out_of_scope:
      - "Swap stage-3 相互重複 (SwapPlacementsUseCase が OccupiedCells を合成、F-P3 anchor)"
      - "Move の excludePlacementId 配線 (MovePlacementUseCase、F-P7 anchor)"
      - "UseCase→ErrorOr マッピング (MapValidation) / repository / undo (AR-07)"
  behavior_contract:                  # crop に無い「制約・順序・衝突」次元
    check_order: "null→throw > 非正grid→OutOfBounds > 上限境界→OutOfBounds > overlap(exclude 適用)→Conflict > Valid"
    bounds: "上限のみ (endX>gridCols||endY>gridRows)。下限(負)は CellPosition ctor が保証=Validate は非チェック"
    self_exclusion: "excludePlacementId は overlap 走査のみスキップ(境界に無影響)。自己 footprint 重複を許す"
    conflict_identity: "最初に重複した existing の Id (collection 反復順依存)"
    conflict_identity_provenance: as-built-incidental   # 反復順依存=偶発か deliberate か未決定 (PV-3 候補)
  ctor_guard:
    inputs: "OccupySize/CellPosition は self-validating VO (throw) = 入力ドメインが型で保証 (crop の plain-data と対照)"
    explicit_guards: "existingPlacements null→ArgumentNullException / gridRows・gridCols<=0→OutOfBounds"
  error_channel:
    result: "PlacementValidationResult (結果オブジェクト: IsValid + Reason enum + ConflictingPlacementId)。null でも ErrorOr でもない"
    precondition_violation: "ArgumentNullException (existing null)"
  vo_method_contract:
    - { method: "OccupiedCells", semantics: "row-major 列挙 (dy 外/dx 内、origin.X+dx, origin.Y+dy)", provenance: deliberate }
  oracle_tests:
    existing: "tests/ViewGrid.Core.Tests/UseCases/PlacementValidatorTests.cs (7)"
    coverage_gaps: [ "非正grid→OutOfBounds", "複数重複時の conflict Id 決定性(反復順依存)", "null existing throw", "exclude 後に別配置と衝突", "multi-cell conflict の Id" ]
```

## メタ問い (この 2 例目で試すこと)
generation_overlay が crop の (値変換+優先+丸め+VO method) から validator の (幾何境界 + overlap/conflict + self-exclusion + 判定順序 + 結果オブジェクト error channel + 自己検証 VO 入力ドメイン) へ汎化するか。新次元 = 判定順序 precedence / conflict 同定の決定性 (反復順依存=有力な gap 候補) / 結果オブジェクト error channel / self-validating 入力 (crop の plain-data と対照)。

## 手順 (PV-2 / PV-3)
1. oracle 硬化: coverage_gaps を追加テスト化 (conflict Id 決定性=first-in-collection / 非正 grid→OutOfBounds / null existing throw / exclude 後別衝突)。実装で green 確認 (= as-built 固定。determinism は as-built-incidental と明記)。
2. enriched spec を凍結 (`placementvalidator-spec.md` = overlay + 上記契約。実装コード片なし)。
3. blind 再生成: 独立生成器に spec のみ (src/tests 非開示)、PlacementValidator.cs を生成。
4. swap 検証: 生成物を src に一時 swap → 既存 7 + 追加 oracle + Application(UseCase) テスト → green か → revert。
5. PV-3 gap feedback: 生成物 vs 実装の差分から overlay/spec に足りなかった次元を抽出 (crop の rounding に相当する validator 版 gap を探す)。

## スコープ外 (本 pilot でやらない)
Swap stage-3/Move 配線/undo の再生成、PlacementValidator 全配置系の再構築、conflict_identity の deliberate 化 (= 人間決定 D 候補)、実 src の恒久変更 (swap は revert)。
