using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドの最終出力サイズ (CanvasSize) を更新する。 配置 (CellPosition / OccupySize) や
/// 重みは変更しない (セル分割の比率は維持されたまま、 各セルの絶対ピクセルサイズだけが変わる)。
/// </summary>
public sealed class UpdateGridCanvasSizeUseCase(IGridCanvasRepository repository)
{
    public const int MinSize = 1;
    public const int MaxSize = 16384;

    public async Task<ErrorOr<GridCanvas>> ExecuteAsync(
        Guid gridId,
        int width,
        int height,
        CancellationToken ct = default)
    {
        if (width < MinSize || width > MaxSize)
            return Error.Validation(
                "Grid.CanvasWidthOutOfRange",
                $"キャンバス幅は {MinSize}〜{MaxSize} px の範囲で指定してください (指定: {width})。");
        if (height < MinSize || height > MaxSize)
            return Error.Validation(
                "Grid.CanvasHeightOutOfRange",
                $"キャンバス高さは {MinSize}〜{MaxSize} px の範囲で指定してください (指定: {height})。");

        var grid = await repository.FindByIdAsync(gridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {gridId} が見つかりません。");

        var newSize = new PixelSize(width, height);
        if (grid.CanvasSize == newSize) return grid;

        var updated = new GridCanvas
        {
            Id = grid.Id,
            Name = grid.Name,
            GridRows = grid.GridRows,
            GridCols = grid.GridCols,
            ColWeights = grid.ColWeights,
            RowWeights = grid.RowWeights,
            ColLocked = grid.ColLocked,
            RowLocked = grid.RowLocked,
            CanvasSize = newSize,
            IsActive = grid.IsActive,
            CreatedAt = grid.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var result = await repository.UpdateAsync(updated, ct);
        return result.IsError ? result.Errors : updated;
    }
}
