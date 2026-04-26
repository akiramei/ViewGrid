using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
            return placement; // 何もしない

        var copy = await copyRepository.FindByIdAsync(placement.CopyId, ct);
        if (copy is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {placement.CopyId} が見つかりません。");

        var grid = await gridRepository.FindByIdAsync(placement.GridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {placement.GridId} が見つかりません。");

        var existing = await placementRepository.FindByGridIdAsync(placement.GridId, ct);
        var descriptors = new List<ExistingPlacement>(existing.Count);
        foreach (var p in existing)
        {
            var pCopy = await copyRepository.FindByIdAsync(p.CopyId, ct);
            descriptors.Add(new ExistingPlacement(p.Id, p.Position, pCopy?.OccupySize ?? OccupySize.OneByOne));
        }

        var validation = PlacementValidator.Validate(
            copy.OccupySize,
            newPosition,
            grid.GridRows,
            grid.GridCols,
            descriptors,
            excludePlacementId: placementId);

        if (!validation.IsValid)
        {
            return validation.Reason switch
            {
                PlacementInvalidReason.OutOfBounds =>
                    Error.Validation("Placement.OutOfBounds", "移動先がグリッド範囲を超えています。"),
                PlacementInvalidReason.Conflict =>
                    Error.Conflict("Placement.Conflict", "移動先が他の配置と重複しています。"),
                _ => Error.Validation("Placement.Invalid", "移動できません。"),
            };
        }

        placement.Position = newPosition;
        var update = await placementRepository.UpdateAsync(placement, ct);
        if (update.IsError)
            return update.Errors;
        return placement;
    }
}
