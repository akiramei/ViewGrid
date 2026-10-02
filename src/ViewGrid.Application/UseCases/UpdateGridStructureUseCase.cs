using System.Collections.Immutable;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドの構造 (行数・列数と、 それに連動する各行・列の重み / ロック) のスナップショット。
/// 行・列の追加とその Undo (= 元の構造へ戻す) で、 同じ形のデータを両方向に渡すために使う。
/// </summary>
/// <param name="Rows">行数。</param>
/// <param name="Cols">列数。</param>
/// <param name="ColWeights">各列の幅比率 (要素数 = <paramref name="Cols"/>)。</param>
/// <param name="RowWeights">各行の高さ比率 (要素数 = <paramref name="Rows"/>)。</param>
/// <param name="ColLocked">各列のロック状態 (要素数 = <paramref name="Cols"/>)。</param>
/// <param name="RowLocked">各行のロック状態 (要素数 = <paramref name="Rows"/>)。</param>
public sealed record GridStructure(
    int Rows,
    int Cols,
    ImmutableArray<int> ColWeights,
    ImmutableArray<int> RowWeights,
    ImmutableArray<bool> ColLocked,
    ImmutableArray<bool> RowLocked)
{
    /// <summary>グリッドの現在の構造を取り出す (ロックが空配列の旧データは全アンロックとして補う)。</summary>
    public static GridStructure From(GridCanvas grid) => new(
        grid.GridRows,
        grid.GridCols,
        grid.ColWeights,
        grid.RowWeights,
        grid.ColLocked.Length == grid.GridCols ? grid.ColLocked : GridCanvas.AllUnlocked(grid.GridCols),
        grid.RowLocked.Length == grid.GridRows ? grid.RowLocked : GridCanvas.AllUnlocked(grid.GridRows));

    /// <summary>
    /// 末尾に 1 行追加した構造を返す。 新しい行の高さ比率は既存の平均 (四捨五入、 最小 1) にして、
    /// 追加しても既存の行が極端に潰れないようにする。 ロックは解除で追加する。
    /// </summary>
    public GridStructure WithRowAdded() => this with
    {
        Rows = Rows + 1,
        RowWeights = RowWeights.Add(AverageWeight(RowWeights)),
        RowLocked = RowLocked.Add(false),
    };

    /// <summary>末尾に 1 列追加した構造を返す (<see cref="WithRowAdded"/> と同じ規則)。</summary>
    public GridStructure WithColumnAdded() => this with
    {
        Cols = Cols + 1,
        ColWeights = ColWeights.Add(AverageWeight(ColWeights)),
        ColLocked = ColLocked.Add(false),
    };

    private static int AverageWeight(ImmutableArray<int> weights) =>
        weights.IsDefaultOrEmpty ? 1 : Math.Max(1, (int)Math.Round(weights.Average()));
}

/// <summary>
/// グリッドの行数・列数を変更する (行・列の追加と、 その Undo)。 既存の配置は一切動かさず、
/// 追加で空きセルが増えるだけ。 縮める (Undo) ときは、 縮めた後の範囲から配置がはみ出すなら拒否する。
/// 作成時と同じ上限 (<see cref="MaxGridDimension"/>) を超える追加も拒否する。
/// </summary>
public sealed class UpdateGridStructureUseCase(
    IGridCanvasRepository gridRepository,
    IGridPlacementRepository placementRepository)
{
    /// <summary>グリッド作成時と同じ、 行数・列数の上限。</summary>
    public const int MaxGridDimension = 20;

    public async Task<ErrorOr<GridCanvas>> ExecuteAsync(
        Guid gridId, GridStructure target, CancellationToken ct = default)
    {
        var grid = await gridRepository.FindByIdAsync(gridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {gridId} が見つかりません。");

        if (target.Rows < 1 || target.Rows > MaxGridDimension)
            return Error.Validation("Grid.RowsOutOfRange", $"行数は 1〜{MaxGridDimension} の範囲で指定してください。");
        if (target.Cols < 1 || target.Cols > MaxGridDimension)
            return Error.Validation("Grid.ColsOutOfRange", $"列数は 1〜{MaxGridDimension} の範囲で指定してください。");
        if (target.ColWeights.Length != target.Cols || target.ColLocked.Length != target.Cols
            || target.RowWeights.Length != target.Rows || target.RowLocked.Length != target.Rows)
            return Error.Validation("Grid.StructureMismatch", "行・列の重みまたはロックの要素数が行数・列数と一致しません。");
        if (target.ColWeights.Any(w => w <= 0) || target.RowWeights.Any(w => w <= 0))
            return Error.Validation("Grid.WeightsMustBePositive", "重みはすべて 1 以上です。");

        // 縮める場合は、 範囲外へはみ出す配置があると配置が宙に浮くので拒否する。
        var placements = await placementRepository.FindByGridIdAsync(gridId, ct);
        var overflowing = placements.Count(p =>
            p.Position.X + p.OccupySize.Width > target.Cols || p.Position.Y + p.OccupySize.Height > target.Rows);
        if (overflowing > 0)
            return Error.Validation(
                "Grid.PlacementsOutOfRange",
                $"行・列を減らすと範囲外になる配置が {overflowing} 件あります。 先に配置を移動または削除してください。");

        var updated = new GridCanvas
        {
            Id = grid.Id,
            Name = grid.Name,
            GridRows = target.Rows,
            GridCols = target.Cols,
            ColWeights = target.ColWeights,
            RowWeights = target.RowWeights,
            ColLocked = target.ColLocked,
            RowLocked = target.RowLocked,
            CanvasSize = grid.CanvasSize,
            CreatedAt = grid.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var result = await gridRepository.UpdateAsync(updated, ct);
        return result.IsError ? result.Errors : updated;
    }
}
