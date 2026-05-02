using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 論理コピーをアクティブグリッドの指定セルに配置する。
/// 境界・既存配置との重複を <see cref="PlacementValidator"/> で検証する。
/// </summary>
public sealed class PlaceImageCopyUseCase(
    IGridCanvasRepository gridRepository,
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository)
{
    public async Task<ErrorOr<GridPlacement>> ExecuteAsync(
        Guid gridId,
        Guid copyId,
        CellPosition position,
        CancellationToken ct = default)
    {
        var grid = await gridRepository.FindByIdAsync(gridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {gridId} が見つかりません。");

        var copy = await copyRepository.FindByIdAsync(copyId, ct);
        if (copy is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {copyId} が見つかりません。");

        var existing = await placementRepository.FindByGridIdAsync(gridId, ct);

        // 既存配置の OccupySize は各 ImageCopy から取得する必要がある（N+1 だが Phase 3-B では許容）
        var existingDescriptors = new ExistingPlacement[existing.Count];
        for (var i = 0; i < existing.Count; i++)
        {
            var p = existing[i];
            var pCopy = await copyRepository.FindByIdAsync(p.CopyId, ct);
            var occupySize = pCopy?.OccupySize ?? OccupySize.OneByOne;
            existingDescriptors[i] = new ExistingPlacement(p.Id, p.Position, occupySize);
        }

        var validation = PlacementValidator.Validate(
            copy.OccupySize,
            position,
            grid.GridRows,
            grid.GridCols,
            existingDescriptors);

        if (!validation.IsValid)
        {
            return validation.Reason switch
            {
                PlacementInvalidReason.OutOfBounds =>
                    Error.Validation("Placement.OutOfBounds", "配置がグリッド範囲を超えています。"),
                PlacementInvalidReason.Conflict =>
                    Error.Conflict("Placement.Conflict", "他の配置と重複しています。"),
                _ => Error.Validation("Placement.Invalid", "配置できません。"),
            };
        }

        var nextOrder = existing.Any() ? existing.Max(p => p.PlacementOrder) + 1 : 1;
        // 新規配置時は元バリアントの OccupySize を初期値として継承する。
        // 配置後はこの placement 固有として独立して編集できる（同じバリアントを別配置で
        // 違う占有セルにすることが可能）。
        var placement = new GridPlacement
        {
            Id = Guid.NewGuid(),
            GridId = gridId,
            CopyId = copyId,
            Position = position,
            OccupySize = copy.OccupySize,
            PlacementOrder = nextOrder,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return await placementRepository.AddAsync(placement, ct);
    }
}
