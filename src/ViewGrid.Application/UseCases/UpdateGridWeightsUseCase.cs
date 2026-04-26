using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドの列・行の重み配列を更新する。配置タブの境界ドラッグからリリース時に呼ばれる。
/// 配置（CellPosition / OccupySize）は変更しない（重みはセル比率だけを変える）。
/// </summary>
public sealed class UpdateGridWeightsUseCase(IGridCanvasRepository repository)
{
    public async Task<ErrorOr<GridCanvas>> ExecuteAsync(
        Guid gridId,
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CancellationToken ct = default)
    {
        var grid = await repository.FindByIdAsync(gridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {gridId} が見つかりません。");

        var newColWeights = grid.ColWeights;
        if (colWeights is not null)
        {
            if (colWeights.Count != grid.GridCols)
                return Error.Validation(
                    "Grid.ColWeightsCountMismatch",
                    $"列重みの要素数 ({colWeights.Count}) が列数 ({grid.GridCols}) と一致しません。");
            if (colWeights.Any(w => w <= 0))
                return Error.Validation("Grid.ColWeightsMustBePositive", "列重みはすべて 1 以上です。");
            newColWeights = colWeights.ToImmutableArray();
        }

        var newRowWeights = grid.RowWeights;
        if (rowWeights is not null)
        {
            if (rowWeights.Count != grid.GridRows)
                return Error.Validation(
                    "Grid.RowWeightsCountMismatch",
                    $"行重みの要素数 ({rowWeights.Count}) が行数 ({grid.GridRows}) と一致しません。");
            if (rowWeights.Any(w => w <= 0))
                return Error.Validation("Grid.RowWeightsMustBePositive", "行重みはすべて 1 以上です。");
            newRowWeights = rowWeights.ToImmutableArray();
        }

        var updated = new GridCanvas
        {
            Id = grid.Id,
            Name = grid.Name,
            GridRows = grid.GridRows,
            GridCols = grid.GridCols,
            ColWeights = newColWeights,
            RowWeights = newRowWeights,
            CanvasSize = grid.CanvasSize,
            IsActive = grid.IsActive,
            CreatedAt = grid.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var result = await repository.UpdateAsync(updated, ct);
        return result.IsError ? result.Errors : updated;
    }
}
