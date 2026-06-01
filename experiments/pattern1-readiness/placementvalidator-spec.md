# PlacementValidator — 生成仕様 (PV-2 blind generation の凍結入力)

> Pattern 1 micro-pilot の 2 例目。**独立生成器に渡す唯一の入力**。
> PV-1 generation_overlay (behavior_contract / error_channel / ctor_guard / vo_method_contract / oracle_tests) から構成。
> **実装コード片は含まない** (意味記述のみ)。生成器はこの spec だけから C# を書く。既存実装/テスト/リポジトリは一切参照しない。

## 生成対象 (1 ファイル = PlacementValidator.cs)
`namespace ViewGrid.Core.UseCases` に以下をすべて生成する:
1. `public static class PlacementValidator` — メソッド `Validate` と `OccupiedCells`。
2. `public readonly record struct ExistingPlacement(Guid PlacementId, CellPosition Position, OccupySize OccupySize)`。
3. `public readonly record struct PlacementValidationResult` — 下記の表面。
4. `public enum PlacementInvalidReason { None = 0, OutOfBounds = 1, Conflict = 2 }`。

呼び出し側 (UseCase 群) が依存するため、**型名・メソッド名・署名・enum 値は下記どおり厳守**。

## 所与の型 (生成しない。これらに対してコンパイルできるように書く。namespace ViewGrid.Core.Entities)
- `OccupySize` — `int Width, int Height` を持つ readonly record struct。**自己検証 VO**: ctor で Width/Height が 1 以上でなければ throw (= 正値が型で保証される)。`OccupySize.OneByOne` = (1,1) の static あり。
- `CellPosition` — `int X, int Y` を持つ readonly record struct (0 ベース)。**自己検証 VO**: ctor で X/Y が負なら throw (= 非負が型で保証される)。

## PlacementValidationResult の表面
- `public bool IsValid { get; }`
- `public PlacementInvalidReason Reason { get; }`
- `public Guid? ConflictingPlacementId { get; }`
- `public static PlacementValidationResult Valid { get; }` = (IsValid=true, Reason=None, ConflictingPlacementId=null)
- `public static PlacementValidationResult OutOfBounds { get; }` = (IsValid=false, Reason=OutOfBounds, ConflictingPlacementId=null)
- `public static PlacementValidationResult Conflict(Guid existingPlacementId)` = (IsValid=false, Reason=Conflict, ConflictingPlacementId=existingPlacementId)
- ctor は private でよい (生成は自由)。**結果は例外でなくこの結果オブジェクトで返す** (前提違反の ArgumentNullException を除く)。

## Validate の契約
署名:
```
public static PlacementValidationResult Validate(
    OccupySize occupySize,
    CellPosition position,
    int gridRows,
    int gridCols,
    IReadOnlyCollection<ExistingPlacement> existingPlacements,
    Guid? excludePlacementId = null)
```

意味 (**判定順序 = この順で評価し、最初に該当したものを返す**):
1. **前提ガード**: `existingPlacements` が null なら `ArgumentNullException` を投げる (結果チャネルとは別)。
2. **非正グリッド**: `gridRows <= 0` または `gridCols <= 0` なら `OutOfBounds`。
3. **上限境界**: `position.X + occupySize.Width > gridCols` または `position.Y + occupySize.Height > gridRows` なら `OutOfBounds`。
   - ★ 下限 (position が負) は **チェックしない**。CellPosition が自己検証 VO で非負を保証するため、Validate は上限のみ見る。
4. **重複 (overlap)**: 新配置の占有セル集合 (= `OccupiedCells(position, occupySize)`) と、各既存配置の占有セル集合が 1 つでも交差したら `Conflict(その既存の PlacementId)`。
   - **self-exclusion**: `excludePlacementId` が非 null のとき、`PlacementId == excludePlacementId.Value` の既存配置は **重複判定からスキップ** (自己 footprint との重複を許す)。境界判定 (手順 2-3) には影響しない。
   - ★ **conflict 同定**: 複数の既存が重複する場合、返す Id は **`existingPlacements` の反復順で最初に重複が見つかった既存の PlacementId**。(列挙順依存。最小 Guid や最大重複面積などの別規則ではない。)
5. 上記いずれにも該当しなければ `Valid`。

## OccupiedCells の契約
署名: `public static IEnumerable<CellPosition> OccupiedCells(CellPosition origin, OccupySize size)`
- `size.Height × size.Width` 個のセルを **row-major** で列挙する: 外ループ `dy = 0..size.Height-1`、内ループ `dx = 0..size.Width-1`、各々 `new CellPosition(origin.X + dx, origin.Y + dy)` を yield。
- 例: `OccupiedCells((2,1), 2x2)` → `(2,1), (3,1), (2,2), (3,2)` の順。

## 観測可能な振る舞い例 (網羅 oracle ではない)
- in-bounds 単一/複数セルで他と重ならない → `Valid`。
- `(2,2) 2x1` を 3x3 グリッドへ → 横にはみ出し `OutOfBounds`。
- 既存 `(1,1) 1x1` がある所へ `(1,1) 1x1` → `Conflict(その Id)`。
- 既存 `(0,0) 1x1` の隣 `(1,0) 1x1` → `Valid` (隣接は重複でない)。
- 既存 `(0,0) 1x1` を `excludePlacementId` に指定し同じ `(0,0)` を検証 → `Valid` (自己除外)。
- 新配置 `(0,0) 2x2`、既存 = [A=(1,0), B=(0,1)] (この配列順) → `Conflict(A の Id)` (反復順で先の A)。
- `gridRows=0` → `OutOfBounds`。`existingPlacements=null` → `ArgumentNullException`。

## 制約
- `using` / namespace を正しく付け、所与の型の署名に合わせる。
- public 表面 (型名/メソッド名/署名/enum 値/PlacementValidationResult のメンバ) は呼び出し側が依存するため厳守。
- 実装の中身は spec の意味を満たせば書き方は自由。**ただし判定順序・self-exclusion・conflict 同定 (反復順で先) は厳守**。
