using ViewGrid.Core.Entities;

namespace ViewGrid.Core.UseCases;

/// <summary>
/// グリッドへの配置の妥当性を検証する純粋関数群。DB アクセスを持たないので
/// テストしやすい。NxM 占有もサポートする（既存配置の <see cref="ExistingPlacement"/>
/// にも OccupySize が含まれる）。
/// </summary>
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
        ArgumentNullException.ThrowIfNull(existingPlacements);

        if (gridRows <= 0 || gridCols <= 0)
            return PlacementValidationResult.OutOfBounds;

        var endX = position.X + occupySize.Width;
        var endY = position.Y + occupySize.Height;
        if (endX > gridCols || endY > gridRows)
            return PlacementValidationResult.OutOfBounds;

        var newCells = OccupiedCells(position, occupySize).ToHashSet();

        foreach (var existing in existingPlacements)
        {
            if (excludePlacementId is not null && existing.PlacementId == excludePlacementId.Value)
                continue;

            foreach (var cell in OccupiedCells(existing.Position, existing.OccupySize))
            {
                if (newCells.Contains(cell))
                    return PlacementValidationResult.Conflict(existing.PlacementId);
            }
        }

        return PlacementValidationResult.Valid;
    }

    /// <summary>占有セルを左上から右下に向けて列挙する。</summary>
    public static IEnumerable<CellPosition> OccupiedCells(CellPosition origin, OccupySize size)
    {
        for (var dy = 0; dy < size.Height; dy++)
        {
            for (var dx = 0; dx < size.Width; dx++)
                yield return new CellPosition(origin.X + dx, origin.Y + dy);
        }
    }
}

/// <summary>配置検証用に既存の配置情報を表す軽量レコード。</summary>
public readonly record struct ExistingPlacement(Guid PlacementId, CellPosition Position, OccupySize OccupySize);

public readonly record struct PlacementValidationResult
{
    public bool IsValid { get; }
    public PlacementInvalidReason Reason { get; }
    public Guid? ConflictingPlacementId { get; }

    private PlacementValidationResult(bool isValid, PlacementInvalidReason reason, Guid? conflict)
    {
        IsValid = isValid;
        Reason = reason;
        ConflictingPlacementId = conflict;
    }

    public static PlacementValidationResult Valid { get; } =
        new(true, PlacementInvalidReason.None, null);

    public static PlacementValidationResult OutOfBounds { get; } =
        new(false, PlacementInvalidReason.OutOfBounds, null);

    public static PlacementValidationResult Conflict(Guid existingPlacementId) =>
        new(false, PlacementInvalidReason.Conflict, existingPlacementId);
}

public enum PlacementInvalidReason
{
    None = 0,
    OutOfBounds = 1,
    Conflict = 2,
}
