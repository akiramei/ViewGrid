using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Geometry;

/// <summary>
/// <see cref="ProtectedRegion"/> と effective Crop の交差結果。 親側塗りつぶし矩形を計算する
/// 際の入力として使う。 region asset 描画は Crop 非依存のため別経路。
/// </summary>
public readonly record struct EffectiveRegion(
    RegionRectFraction SourceRect,
    RegionRectFraction CropLocalRect);

/// <summary>
/// <see cref="ProtectedRegion"/> の幾何計算を純粋関数で提供する。
/// </summary>
/// <remarks>
/// <para>役割:</para>
/// <list type="bullet">
///   <item><see cref="Intersect"/>: 親側塗りつぶし計算用。 region と effective Crop の交差から
///     「parent の可視部分のうち region に属する領域」 を切り出す</item>
///   <item><see cref="ComputeSourceToCellScale"/>: region asset の描画サイズ計算用。
///     親画像の source-pixel→cell-pixel スケール係数を返す (回転による軸入れ替えを考慮)</item>
/// </list>
/// </remarks>
public static class RegionGeometry
{
    /// <summary>
    /// <paramref name="region"/> (元画像 0–1) と <paramref name="effectiveCrop"/> (元画像 0–1) の
    /// 交差を計算する。 交差なし → <c>null</c>。 交差あり → 元画像座標系の交差矩形 (<see cref="EffectiveRegion.SourceRect"/>)
    /// と Crop-local 座標系の交差矩形 (<see cref="EffectiveRegion.CropLocalRect"/>) を返す。
    /// </summary>
    /// <remarks>
    /// effectiveCrop が <see cref="CropFraction.Full"/> ((0,0,1,1)) のときは SourceRect = region、
    /// CropLocalRect も同じ値 (Crop による座標変換は恒等)。
    /// </remarks>
    public static EffectiveRegion? Intersect(
        RegionRectFraction region,
        CropFraction effectiveCrop)
    {
        // 退化 Crop は描画対象外 (定義上 0 width / 0 height は 「見える領域なし」)
        if (effectiveCrop.Width <= 0 || effectiveCrop.Height <= 0) return null;

        // 元画像座標の交差 bbox
        var x0 = Math.Max(region.X, effectiveCrop.X);
        var y0 = Math.Max(region.Y, effectiveCrop.Y);
        var x1 = Math.Min(region.X + region.Width, effectiveCrop.X + effectiveCrop.Width);
        var y1 = Math.Min(region.Y + region.Height, effectiveCrop.Y + effectiveCrop.Height);
        if (x1 <= x0 || y1 <= y0) return null;

        var sourceRect = new RegionRectFraction(x0, y0, x1 - x0, y1 - y0);

        // Crop-local 座標 (Crop 内で 0–1 にリスケール)
        var cropLocalRect = new RegionRectFraction(
            (x0 - effectiveCrop.X) / effectiveCrop.Width,
            (y0 - effectiveCrop.Y) / effectiveCrop.Height,
            (x1 - x0) / effectiveCrop.Width,
            (y1 - y0) / effectiveCrop.Height);

        return new EffectiveRegion(sourceRect, cropLocalRect);
    }

    /// <summary>
    /// 親画像の 「source-pixel → cell-pixel」 スケール係数を返す。 region asset を親と同じ倍率で
    /// 描画するために使う (UniformContain なら Sx == Sy、 Fill なら異なり得る)。
    /// </summary>
    /// <param name="transform">親の <see cref="ImageTransform"/>。 Cw90 / Cw270 では source X 軸 ↔
    /// transformed Y 軸が入れ替わる。 Flip は cell 内の見え方には影響するが scale magnitude には影響しない
    /// ので、 ここでは Rotation だけ参照する。</param>
    /// <param name="transformedSrcRectWidth">renderer が <see cref="ImageCropResolver"/> 経由で
    /// 計算する「実際に描画される transformed image 領域」 の幅 (= 可視 src rect の幅、 transformed coord)。</param>
    /// <param name="transformedSrcRectHeight">同上、 高さ。</param>
    /// <param name="dstRectWidth">cell 内に描画される dest 矩形の幅 (cell-local pixel)。</param>
    /// <param name="dstRectHeight">同上、 高さ。</param>
    /// <returns>(Sx, Sy): source の X 軸 / Y 軸 1 px が cell 上で何 px に相当するか。
    /// <paramref name="transformedSrcRectWidth"/> または <paramref name="transformedSrcRectHeight"/> が 0 以下なら (0, 0)。</returns>
    public static (double Sx, double Sy) ComputeSourceToCellScale(
        ImageTransform transform,
        double transformedSrcRectWidth,
        double transformedSrcRectHeight,
        double dstRectWidth,
        double dstRectHeight)
    {
        if (transformedSrcRectWidth <= 0 || transformedSrcRectHeight <= 0) return (0.0, 0.0);

        var sxTransformed = dstRectWidth / transformedSrcRectWidth;
        var syTransformed = dstRectHeight / transformedSrcRectHeight;

        // 90° / 270° で source X 軸 ↔ transformed Y 軸 が swap される。
        return transform.Rotation is Rotation.Cw90 or Rotation.Cw270
            ? (syTransformed, sxTransformed)
            : (sxTransformed, syTransformed);
    }
}
