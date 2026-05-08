namespace ViewGrid.Core.Entities;

/// <summary>
/// 元画像座標系における 「保護領域 (<see cref="ProtectedRegion"/>)」 の bbox を 0–1 の比率で
/// 保持する値。 <see cref="ManualCropFraction"/> と形は同じだが意味は別:
/// <list type="bullet">
///   <item><see cref="ManualCropFraction"/>: 画像全体の有効範囲を置き換える (= Crop)</item>
///   <item><see cref="RegionRectFraction"/>: 見えている画像の一部を Overlay として持ち上げる (= 保護)</item>
/// </list>
/// 同じ <c>CropFraction</c> 系を流用すると Phase 2 以降 (Polygon / Snap90 / FollowParent 等) で
/// 意味の混線を起こすため、 別型として固定する。
/// </summary>
/// <remarks>
/// すべて 0.0–1.0。 適用順序は
/// <c>元画像 → ManualCrop / AutoCrop で実効領域決定 → Region と交差判定 → 親側 fill 塗り + Overlay 描画</c>
/// (元画像座標系で完結)。
/// </remarks>
public readonly record struct RegionRectFraction(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// fraction を整数ピクセル bbox に展開する (軸サイズで掛けて round)。
    /// <see cref="ManualCropFraction.ToPixelBbox"/> と同じ clamp 規則。
    /// </summary>
    public (int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)
    {
        var x = (int)System.Math.Clamp(System.Math.Round(X * width), 0, width);
        var y = (int)System.Math.Clamp(System.Math.Round(Y * height), 0, height);
        var w = (int)System.Math.Clamp(System.Math.Round(Width * width), 0, width - x);
        var h = (int)System.Math.Clamp(System.Math.Round(Height * height), 0, height - y);
        return (x, y, w, h);
    }

    /// <summary>
    /// 値域が 0–1 内に収まり、 幅 / 高さが正値の有効な矩形か。
    /// 不正値は 0 width / negative coord 等を含む。 ロード時 / 保存時のガードに使う。
    /// </summary>
    public bool IsValid(double tolerance = 1e-9) =>
        X >= -tolerance
        && Y >= -tolerance
        && Width > tolerance
        && Height > tolerance
        && X + Width <= 1.0 + tolerance
        && Y + Height <= 1.0 + tolerance;
}
