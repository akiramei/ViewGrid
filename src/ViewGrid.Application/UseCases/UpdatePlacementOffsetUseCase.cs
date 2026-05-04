using ErrorOr;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 配置のピクセル微調整（<see cref="ViewGrid.Core.Entities.GridPlacement.PixelOffsetX"/> /
/// <c>PixelOffsetY</c>）のみを更新する。境界検証は行わない（セル外へのはみ出しは
/// 意図された設計で、Renderer もクリップしない）。極端な値の上限丸めは VM 側で行う。
/// </summary>
public sealed class UpdatePlacementOffsetUseCase(IGridPlacementRepository placementRepository)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(
        Guid placementId,
        int pixelOffsetX,
        int pixelOffsetY,
        CancellationToken ct = default)
    {
        var placement = await placementRepository.FindByIdAsync(placementId, ct);
        if (placement is null)
            return Error.NotFound("Placement.NotFound", $"GridPlacement {placementId} が見つかりません。");

        placement.PixelOffsetX = pixelOffsetX;
        placement.PixelOffsetY = pixelOffsetY;
        return await placementRepository.UpdateAsync(placement, ct);
    }
}
