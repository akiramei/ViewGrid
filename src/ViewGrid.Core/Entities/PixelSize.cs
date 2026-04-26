using System;

namespace ViewGrid.Core.Entities;

/// <summary>
/// ピクセル単位のサイズ（画像解像度、キャンバスサイズ等）。
/// </summary>
public readonly record struct PixelSize
{
    public int Width { get; }
    public int Height { get; }

    public PixelSize(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        Width = width;
        Height = height;
    }
}
