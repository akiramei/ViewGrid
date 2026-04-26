using System;

namespace ViewGrid.Core.Entities;

/// <summary>
/// 画像が占有するセルサイズ（NxM、矩形のみ）。
/// </summary>
public readonly record struct OccupySize
{
    public int Width { get; }
    public int Height { get; }

    public OccupySize(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        Width = width;
        Height = height;
    }

    public static OccupySize OneByOne { get; } = new(1, 1);
}
