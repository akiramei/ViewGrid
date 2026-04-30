namespace ViewGrid.Core.Entities;

/// <summary>
/// AutoCrop 走査結果を「画像サイズに対する 0–1 の比率」として保持する座標系非依存の bbox。
/// <para>
/// 原画像で 1 度だけ走査して得た bbox を比率化することで、Renderer（原画像サイズに掛ける）
/// と View（サムネサイズに掛ける）が同一の結果を共有できる（圧縮済みサムネで再走査して
/// 精度差が出る問題を回避）。
/// </para>
/// <para>
/// すべて 0.0–1.0。<c>(0, 0, 1, 1)</c> はクロップ無効（全領域）。
/// </para>
/// </summary>
public readonly record struct AutoCropFraction(double X, double Y, double Width, double Height)
{
    /// <summary>クロップ無効（全領域）のセンチネル値。</summary>
    public static AutoCropFraction Full { get; } = new(0.0, 0.0, 1.0, 1.0);

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
