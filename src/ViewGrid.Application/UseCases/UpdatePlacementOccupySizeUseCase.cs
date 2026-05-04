using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 配置単位の OccupySize（占有セルサイズ）を更新する。
/// <para>
/// OccupySize は配置（GridPlacement）固有の特性で、同じバリアント（ImageCopy）を
/// 別グリッド・別セルに配置しても、それぞれ独立した占有セルを設定できる。本 UseCase は
/// グリッド境界 / 他配置との重複を <see cref="PlacementValidator"/> で検証して、
/// 違反があれば永続化しない。
/// </para>
/// </summary>
public sealed class UpdatePlacementOccupySizeUseCase(
    IGridPlacementRepository placementRepository,
    IGridCanvasRepository gridRepository)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(
        Guid placementId,
        OccupySize newSize,
        CancellationToken ct = default)
    {
        var placement = await placementRepository.FindByIdAsync(placementId, ct);
        if (placement is null)
            return Error.NotFound("Placement.NotFound", $"GridPlacement {placementId} が見つかりません。");

        if (placement.OccupySize == newSize)
            return Result.Success;

        var grid = await gridRepository.FindByIdAsync(placement.GridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {placement.GridId} が見つかりません。");

        var allInGrid = await placementRepository.FindByGridIdAsync(placement.GridId, ct);
        var existing = new List<ExistingPlacement>(allInGrid.Count);
        foreach (var p in allInGrid)
        {
            existing.Add(new ExistingPlacement(p.Id, p.Position, p.OccupySize));
        }

        var validation = PlacementValidator.Validate(
            newSize,
            placement.Position,
            grid.GridRows,
            grid.GridCols,
            existing,
            excludePlacementId: placement.Id);

        if (!validation.IsValid)
        {
            return validation.Reason switch
            {
                PlacementInvalidReason.OutOfBounds => Error.Validation(
                    "Placement.OccupySizeOutOfBounds",
                    $"占有セル {newSize.Width}×{newSize.Height} は配置 ({placement.Position.X},{placement.Position.Y}) でグリッド「{grid.Name}」({grid.GridCols}×{grid.GridRows}) の範囲外になります。"),
                PlacementInvalidReason.Conflict => Error.Validation(
                    "Placement.OccupySizeConflict",
                    $"占有セル {newSize.Width}×{newSize.Height} は配置 ({placement.Position.X},{placement.Position.Y}) で他の配置と重なります。"),
                _ => Error.Validation("Placement.OccupySizeInvalid", "占有セルの変更が無効です。"),
            };
        }

        placement.OccupySize = newSize;
        return await placementRepository.UpdateAsync(placement, ct);
    }
}
