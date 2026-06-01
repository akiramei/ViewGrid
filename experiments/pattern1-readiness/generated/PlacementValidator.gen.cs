// === PV-2 blind generation artifact (NOT compiled in place; under experiments/) ===
// placementvalidator-spec.md のみから独立生成器が blind 生成 (0 tool use = リポジトリ未参照)。
// 実装との比較は placementvalidator-micro-pilot-RESULT.md。判定順序/self-exclusion/conflict 同定(反復順で先)/
// row-major まで意味一致。差は構造的のみ (block namespace vs file-scoped、冗長な using System.Linq)。
// 検証: src/ViewGrid.Core/UseCases/PlacementValidator.cs へ一時 swap → 全テスト → revert。
using System;
using System.Collections.Generic;
using System.Linq;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.UseCases
{
    public enum PlacementInvalidReason
    {
        None = 0,
        OutOfBounds = 1,
        Conflict = 2
    }

    public readonly record struct ExistingPlacement(
        Guid PlacementId,
        CellPosition Position,
        OccupySize OccupySize);

    public readonly record struct PlacementValidationResult
    {
        public bool IsValid { get; }
        public PlacementInvalidReason Reason { get; }
        public Guid? ConflictingPlacementId { get; }

        private PlacementValidationResult(
            bool isValid,
            PlacementInvalidReason reason,
            Guid? conflictingPlacementId)
        {
            IsValid = isValid;
            Reason = reason;
            ConflictingPlacementId = conflictingPlacementId;
        }

        public static PlacementValidationResult Valid { get; } =
            new PlacementValidationResult(true, PlacementInvalidReason.None, null);

        public static PlacementValidationResult OutOfBounds { get; } =
            new PlacementValidationResult(false, PlacementInvalidReason.OutOfBounds, null);

        public static PlacementValidationResult Conflict(Guid existingPlacementId) =>
            new PlacementValidationResult(false, PlacementInvalidReason.Conflict, existingPlacementId);
    }

    public static class PlacementValidator
    {
        public static PlacementValidationResult Validate(
            OccupySize occupySize,
            CellPosition position,
            int gridRows,
            int gridCols,
            IReadOnlyCollection<ExistingPlacement> existingPlacements,
            Guid? excludePlacementId = null)
        {
            // 1. 前提ガード
            if (existingPlacements is null)
            {
                throw new ArgumentNullException(nameof(existingPlacements));
            }

            // 2. 非正グリッド
            if (gridRows <= 0 || gridCols <= 0)
            {
                return PlacementValidationResult.OutOfBounds;
            }

            // 3. 上限境界 (下限は CellPosition が非負を保証するためチェックしない)
            if (position.X + occupySize.Width > gridCols ||
                position.Y + occupySize.Height > gridRows)
            {
                return PlacementValidationResult.OutOfBounds;
            }

            // 4. 重複
            var newCells = new HashSet<CellPosition>(OccupiedCells(position, occupySize));

            foreach (var existing in existingPlacements)
            {
                if (excludePlacementId.HasValue &&
                    existing.PlacementId == excludePlacementId.Value)
                {
                    continue;
                }

                foreach (var cell in OccupiedCells(existing.Position, existing.OccupySize))
                {
                    if (newCells.Contains(cell))
                    {
                        return PlacementValidationResult.Conflict(existing.PlacementId);
                    }
                }
            }

            // 5. Valid
            return PlacementValidationResult.Valid;
        }

        public static IEnumerable<CellPosition> OccupiedCells(CellPosition origin, OccupySize size)
        {
            for (int dy = 0; dy < size.Height; dy++)
            {
                for (int dx = 0; dx < size.Width; dx++)
                {
                    yield return new CellPosition(origin.X + dx, origin.Y + dy);
                }
            }
        }
    }
}
