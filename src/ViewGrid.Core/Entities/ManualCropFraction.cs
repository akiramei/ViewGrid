namespace ViewGrid.Core.Entities;

/// <summary>
/// 任意矩形トリミング (ManualCrop) の bbox を「画像サイズに対する 0–1 の比率」で保持する
/// 座標系非依存の値。<see cref="AutoCropFraction"/> と同じ設計で、Renderer / View / Use case の
/// 3 経路が同一比率を共有できる。
/// <para>
/// すべて 0.0–1.0。<c>(0, 0, 1, 1)</c> はクロップ無効（全領域）。
/// 適用順序は <c>元画像 → ManualCrop → 回転・反転 → ScalingMode/Alignment</c>（元画像座標系で完結）。
/// </para>
/// </summary>
public readonly record struct ManualCropFraction(double X, double Y, double Width, double Height)
{
    /// <summary>クロップ無効（全領域）のセンチネル値。</summary>
    public static ManualCropFraction Full { get; } = new(0.0, 0.0, 1.0, 1.0);

    /// <summary>fraction を整数ピクセル bbox に展開する（軸サイズで掛けて round）。</summary>
    public (int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)
    {
        var x = (int)System.Math.Clamp(System.Math.Round(X * width), 0, width);
        var y = (int)System.Math.Clamp(System.Math.Round(Y * height), 0, height);
        var w = (int)System.Math.Clamp(System.Math.Round(Width * width), 0, width - x);
        var h = (int)System.Math.Clamp(System.Math.Round(Height * height), 0, height - y);
        return (x, y, w, h);
    }

    /// <summary>クロップ無効（fullness 1.0 に近い）か。</summary>
    public bool IsFull(double tolerance = 1e-6) =>
        System.Math.Abs(X) < tolerance && System.Math.Abs(Y) < tolerance
        && System.Math.Abs(Width - 1.0) < tolerance && System.Math.Abs(Height - 1.0) < tolerance;
}
