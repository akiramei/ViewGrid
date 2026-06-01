// === F-P10 blind generation artifact (NOT compiled; under experiments/) ===
// crop-resolver-spec.md のみから独立生成器が生成した CropFraction。既存実装は非開示で生成。
// 実装との発散: ToPixelBbox の round が AwayFromZero (実装は Math.Round 既定=ToEven/銀行家丸め)。
//   → spec が丸め規則を未指定だったため (d-3 フィードバック候補)。
using System;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Entities;

/// <summary>
/// 実効的なクロップ bbox の比率 (0–1)。AutoCrop/ManualCrop 双方からの変換先で、
/// 源を意識せず使える統一型。座標系非依存。
/// </summary>
public readonly record struct CropFraction(double X, double Y, double Width, double Height)
{
    /// <summary>クロップ無効 (全領域) のセンチネル。</summary>
    public static CropFraction Full => new(0.0, 0.0, 1.0, 1.0);

    /// <summary>
    /// X,Y がともに 0 近傍 (絶対値 &lt; tolerance) かつ Width,Height がともに 1.0 近傍
    /// (|値-1| &lt; tolerance) のとき true (= クロップ無効)。
    /// </summary>
    public bool IsFull(double tolerance = 1e-6)
        => Math.Abs(X) < tolerance
        && Math.Abs(Y) < tolerance
        && Math.Abs(Width - 1.0) < tolerance
        && Math.Abs(Height - 1.0) < tolerance;

    /// <summary>
    /// 比率を整数ピクセル bbox へ展開する。w/h の上限は残り (width-x / height-y) であり、
    /// 画像外へはみ出さない。
    /// </summary>
    public (int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)
    {
        int x = Clamp(Round(X * width), 0, width);
        int y = Clamp(Round(Y * height), 0, height);
        int w = Clamp(Round(Width * width), 0, width - x);
        int h = Clamp(Round(Height * height), 0, height - y);
        return (x, y, w, h);
    }

    /// <summary>AutoCropFraction を実効クロップ比率へ写像する。</summary>
    public static CropFraction From(AutoCropFraction f)
        => new(f.X, f.Y, f.Width, f.Height);

    /// <summary>ManualCropFraction を実効クロップ比率へ写像する。</summary>
    public static CropFraction From(ManualCropFraction f)
        => new(f.X, f.Y, f.Width, f.Height);

    private static int Round(double value)
        => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static int Clamp(int value, int min, int max)
    {
        if (max < min)
        {
            max = min;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
