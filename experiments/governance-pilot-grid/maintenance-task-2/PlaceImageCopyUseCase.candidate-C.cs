// === maintenance-task-2 / SUBJECT 2 candidate-C ===
// 保守タスク: 「配置をスマートにする — 画像の形に合わせて初期の占有セルを決める」
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 論理コピーをアクティブグリッドの指定セルに配置する。
/// 境界・既存配置との重複を <see cref="PlacementValidator"/> で検証する。
/// 配置時は画像の縦横比に合わせて初期占有セルを自動決定する。
/// </summary>
public sealed class PlaceImageCopyUseCase(
    IGridCanvasRepository gridRepository,
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository,
    IImageAssetRepository assetRepository)
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

        // OccupySize は配置固有なので、各 placement から直接取得する。
        var existingDescriptors = new ExistingPlacement[existing.Count];
        for (var i = 0; i < existing.Count; i++)
        {
            var p = existing[i];
            existingDescriptors[i] = new ExistingPlacement(p.Id, p.Position, p.OccupySize);
        }

        // 画像の縦横比に合わせて初期占有セルを決める:
        //   横長 → 2×1、縦長 → 1×2、ほぼ正方形 → 1×1。
        // 画像形状にフィットした配置になり、ユーザーが手で占有セルを直す手間が減る。
        var asset = await assetRepository.FindByIdAsync(copy.AssetId, ct);
        var initialOccupy = ResolveInitialOccupy(asset);

        var validation = PlacementValidator.Validate(
            initialOccupy,
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
        var placement = new GridPlacement
        {
            Id = Guid.NewGuid(),
            GridId = gridId,
            CopyId = copyId,
            Position = position,
            OccupySize = initialOccupy,
            PlacementOrder = nextOrder,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return await placementRepository.AddAsync(placement, ct);
    }

    private static OccupySize ResolveInitialOccupy(ImageAsset? asset)
    {
        if (asset is null)
            return OccupySize.OneByOne;

        var w = asset.Size.Width;
        var h = asset.Size.Height;
        if (w >= h * 1.3)
            return new OccupySize(2, 1);   // 横長
        if (h >= w * 1.3)
            return new OccupySize(1, 2);   // 縦長
        return OccupySize.OneByOne;        // ほぼ正方形
    }
}
