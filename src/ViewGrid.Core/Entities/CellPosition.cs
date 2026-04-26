using System;

namespace ViewGrid.Core.Entities;

/// <summary>
/// グリッド上のセル位置（0 ベース）。
/// </summary>
public readonly record struct CellPosition
{
    public int X { get; }
    public int Y { get; }

    public CellPosition(int x, int y)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), x, "X must be non-negative.");
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y), y, "Y must be non-negative.");
        X = x;
        Y = y;
    }
}
