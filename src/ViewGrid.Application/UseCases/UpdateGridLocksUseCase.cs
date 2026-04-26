using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドの列・行のロック配列を更新する。ロック中の列/行は
/// <see cref="FitGridWeightToPlacementUseCase"/> でのフィット動作で重みが変動せず、
/// 余白分配でも飛ばされる。配置（CellPosition / OccupySize）は変更しない。
/// </summary>
public sealed class UpdateGridLocksUseCase(IGridCanvasRepository repository)
{
    public async Task<ErrorOr<GridCanvas>> ExecuteAsync(
        Guid gridId,
        IReadOnlyList<bool>? colLocked,
        IReadOnlyList<bool>? rowLocked,
        CancellationToken ct = default)
    {
        var grid = await repository.FindByIdAsync(gridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {gridId} が見つかりません。");

        var newColLocked = grid.ColLocked;
        if (colLocked is not null)
        {
            if (colLocked.Count != grid.GridCols)
                return Error.Validation(
                    "Grid.ColLockedCountMismatch",
                    $"列ロックの要素数 ({colLocked.Count}) が列数 ({grid.GridCols}) と一致しません。");
            newColLocked = [.. colLocked];
        }

        var newRowLocked = grid.RowLocked;
        if (rowLocked is not null)
        {
            if (rowLocked.Count != grid.GridRows)
                return Error.Validation(
                    "Grid.RowLockedCountMismatch",
                    $"行ロックの要素数 ({rowLocked.Count}) が行数 ({grid.GridRows}) と一致しません。");
            newRowLocked = [.. rowLocked];
        }

        var updated = new GridCanvas
        {
            Id = grid.Id,
            Name = grid.Name,
            GridRows = grid.GridRows,
            GridCols = grid.GridCols,
            ColWeights = grid.ColWeights,
            RowWeights = grid.RowWeights,
            ColLocked = newColLocked,
            RowLocked = newRowLocked,
            CanvasSize = grid.CanvasSize,
            IsActive = grid.IsActive,
            CreatedAt = grid.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var result = await repository.UpdateAsync(updated, ct);
        return result.IsError ? result.Errors : updated;
    }
}
