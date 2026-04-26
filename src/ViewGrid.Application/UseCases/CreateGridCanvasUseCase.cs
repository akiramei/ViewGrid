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
/// 新規グリッドキャンバスを作成する。バリデーションに通れば DB に保存し、
/// 要求に応じてアクティブ化する。
/// </summary>
public sealed class CreateGridCanvasUseCase(IGridCanvasRepository repository)
{
    private const int MinDimension = 1;
    private const int MaxGridDimension = 20;
    private const int MaxCanvasPixels = 8192;

    public async Task<ErrorOr<GridCanvas>> ExecuteAsync(
        CreateGridCanvasRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("GridCanvas.NameRequired", "グリッド名を入力してください。");

        if (request.Rows < MinDimension || request.Rows > MaxGridDimension)
            return Error.Validation("GridCanvas.RowsOutOfRange", $"行数は {MinDimension}〜{MaxGridDimension} の範囲で指定してください。");

        if (request.Cols < MinDimension || request.Cols > MaxGridDimension)
            return Error.Validation("GridCanvas.ColsOutOfRange", $"列数は {MinDimension}〜{MaxGridDimension} の範囲で指定してください。");

        if (request.CanvasWidth < MinDimension || request.CanvasWidth > MaxCanvasPixels)
            return Error.Validation("GridCanvas.WidthOutOfRange", $"キャンバス幅は {MinDimension}〜{MaxCanvasPixels}px の範囲で指定してください。");

        if (request.CanvasHeight < MinDimension || request.CanvasHeight > MaxCanvasPixels)
            return Error.Validation("GridCanvas.HeightOutOfRange", $"キャンバス高さは {MinDimension}〜{MaxCanvasPixels}px の範囲で指定してください。");

        // 重み配列の検証: 指定なしなら均等、指定ありなら要素数一致 + すべて正
        var colWeights = NormalizeWeights(request.ColWeights, request.Cols, "列");
        if (colWeights.IsError) return colWeights.Errors;
        var rowWeights = NormalizeWeights(request.RowWeights, request.Rows, "行");
        if (rowWeights.IsError) return rowWeights.Errors;

        var now = DateTimeOffset.UtcNow;
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            GridRows = request.Rows,
            GridCols = request.Cols,
            ColWeights = colWeights.Value,
            RowWeights = rowWeights.Value,
            CanvasSize = new PixelSize(request.CanvasWidth, request.CanvasHeight),
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var addResult = await repository.AddAsync(grid, ct);
        if (addResult.IsError)
            return addResult.Errors;

        if (request.SetAsActive)
        {
            var activate = await repository.SetActiveAsync(grid.Id, ct);
            if (activate.IsError)
                return activate.Errors;
            grid.IsActive = true;
        }

        return grid;
    }

    private static ErrorOr<ImmutableArray<int>> NormalizeWeights(IReadOnlyList<int>? weights, int expectedCount, string axisLabel)
    {
        if (weights is null || weights.Count == 0)
            return GridCanvas.UniformWeights(expectedCount);

        if (weights.Count != expectedCount)
            return Error.Validation(
                "GridCanvas.WeightsCountMismatch",
                $"{axisLabel}比率の要素数 ({weights.Count}) が{axisLabel}数 ({expectedCount}) と一致しません。");

        if (weights.Any(w => w <= 0))
            return Error.Validation(
                "GridCanvas.WeightsMustBePositive",
                $"{axisLabel}比率はすべて 1 以上の整数で指定してください。");

        return weights.ToImmutableArray();
    }
}

public sealed record CreateGridCanvasRequest
{
    public required string Name { get; init; }
    public required int Rows { get; init; }
    public required int Cols { get; init; }
    public required int CanvasWidth { get; init; }
    public required int CanvasHeight { get; init; }
    /// <summary>列の幅比率。null なら均等。</summary>
    public IReadOnlyList<int>? ColWeights { get; init; }
    /// <summary>行の高さ比率。null なら均等。</summary>
    public IReadOnlyList<int>? RowWeights { get; init; }
    public bool SetAsActive { get; init; } = true;
}
