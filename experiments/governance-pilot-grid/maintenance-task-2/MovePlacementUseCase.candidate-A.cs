// === maintenance-task-2 / SUBJECT 1 candidate-A ===
// 保守タスク: 「MovePlacementUseCase を可読性向上のため整理」
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 既存の配置を別セルへ移動する。配置自身を除外して境界・重複を検証する。
/// </summary>
public sealed class MovePlacementUseCase(
    IGridCanvasRepository gridRepository,
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository)
{
    public async Task<ErrorOr<GridPlacement>> ExecuteAsync(
        Guid placementId,
        CellPosition newPosition,
        CancellationToken ct = default)
    {
        var placement = await placementRepository.FindByIdAsync(placementId, ct);
        if (placement is null)
            return Error.NotFound("Placement.NotFound", $"GridPlacement {placementId} が見つかりません。");

        if (placement.Position == newPosition)
            return placement; // 同一セルなら何もしない

        var copy = await copyRepository.FindByIdAsync(placement.CopyId, ct);
        if (copy is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {placement.CopyId} が見つかりません。");

        var grid = await gridRepository.FindByIdAsync(placement.GridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {placement.GridId} が見つかりません。");

        var descriptors = await BuildExistingDescriptorsAsync(placement.GridId, ct);

        var validation = PlacementValidator.Validate(
            placement.OccupySize,
            newPosition,
            grid.GridRows,
            grid.GridCols,
            descriptors,
            excludePlacementId: placementId);

        if (!validation.IsValid)
            return ToError(validation.Reason);

        placement.Position = newPosition;
        var update = await placementRepository.UpdateAsync(placement, ct);
        return update.IsError ? update.Errors : placement;
    }

    // OccupySize は配置単位の固有特性なので各 placement から直接取得する。
    private async Task<List<ExistingPlacement>> BuildExistingDescriptorsAsync(Guid gridId, CancellationToken ct)
    {
        var existing = await placementRepository.FindByGridIdAsync(gridId, ct);
        var descriptors = new List<ExistingPlacement>(existing.Count);
        foreach (var p in existing)
            descriptors.Add(new ExistingPlacement(p.Id, p.Position, p.OccupySize));
        return descriptors;
    }

    private static Error ToError(PlacementInvalidReason reason) => reason switch
    {
        PlacementInvalidReason.OutOfBounds =>
            Error.Validation("Placement.OutOfBounds", "移動先がグリッド範囲を超えています。"),
        PlacementInvalidReason.Conflict =>
            Error.Conflict("Placement.Conflict", "移動先が他の配置と重複しています。"),
        _ => Error.Validation("Placement.Invalid", "移動できません。"),
    };
}
