using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ViewGrid.Core.Entities;

/// <summary>
/// 配置対象のグリッドキャンバス。複数持てるが編集対象（アクティブ）は常に 1 つ。
/// 列・行の比率を <see cref="ColWeights"/> / <see cref="RowWeights"/> で指定でき、
/// 不均等なコマ割り（例: 2:1:1）にも対応する。
/// </summary>
public sealed class GridCanvas
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }

    /// <summary>グリッドの行数（縦方向）。</summary>
    public required int GridRows { get; init; }

    /// <summary>グリッドの列数（横方向）。</summary>
    public required int GridCols { get; init; }

    /// <summary>各列の幅比率（要素数 = <see cref="GridCols"/>）。すべて正の整数。</summary>
    public required ImmutableArray<int> ColWeights { get; init; }

    /// <summary>各行の高さ比率（要素数 = <see cref="GridRows"/>）。すべて正の整数。</summary>
    public required ImmutableArray<int> RowWeights { get; init; }

    /// <summary>最終出力サイズ（ピクセル）。</summary>
    public required PixelSize CanvasSize { get; init; }

    public required bool IsActive { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>すべての列・行を均等（重み 1）にする既定の重み配列を返す。</summary>
    public static ImmutableArray<int> UniformWeights(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "count must be positive.");
        return [.. Enumerable.Repeat(1, count)];
    }
}
