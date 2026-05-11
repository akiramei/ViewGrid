using System.Collections.Generic;
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

    /// <summary>
    /// 親側塗り矩形 (cell-local 表示座標、 作成キャンバス px、 cell でクリップ済み) を計算する。
    /// renderer の <c>SkiaGridImageRenderer.ComputeRegionParentFillRect</c> と同じ式の純粋関数版。
    /// region が effective Crop の外、 Transform 後 src 矩形外、 cell の外のいずれかなら <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 用途:
    /// <list type="bullet">
    ///   <item>live preview overlay の位置決め (GridCanvasView)</item>
    ///   <item>新規 region 追加時の「親側塗りと同じ位置に asset を初期配置」 用に <c>OffsetXPx/Y</c>
    ///     を逆算 (Inspector → CopyProperties)</item>
    /// </list>
    /// </remarks>
    public static (double X, double Y, double W, double H)? ComputeParentFillCanvasRect(
        PixelSize canvasSize, int cols, int rows,
        IReadOnlyList<int> colWeights, IReadOnlyList<int> rowWeights,
        CellPosition position, OccupySize occupySize,
        int pixelOffsetX, int pixelOffsetY,
        ImageTransform transform,
        ScalingMode scalingMode, Alignment alignment,
        CropFraction? effectiveCrop,
        int sourceWidth, int sourceHeight,
        RegionRectFraction regionRect)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return null;

        // 1. cellRect (PixelOffset 非適用 = cell 境界クリップ用)
        var cellRect = PlacementGeometry.ComputeDestRect(
            canvasSize, cols, rows, colWeights, rowWeights,
            position, occupySize, 0, 0);
        if (cellRect.Width <= 0 || cellRect.Height <= 0) return null;

        // 2. dest (PixelOffset 適用 = ScalingMode + Alignment 計算用)
        var dest = PlacementGeometry.ComputeDestRect(
            canvasSize, cols, rows, colWeights, rowWeights,
            position, occupySize, pixelOffsetX, pixelOffsetY);
        if (dest.Width <= 0 || dest.Height <= 0) return null;

        // 3. region ∩ effectiveCrop (元画像 0–1)
        var crop = effectiveCrop ?? new CropFraction(0, 0, 1, 1);
        var intersect = Intersect(regionRect, crop);
        if (intersect is null) return null;

        // 4. 元画像 pixel 矩形に展開
        var (sx, sy, sw, sh) = intersect.Value.SourceRect.ToPixelBbox(sourceWidth, sourceHeight);
        if (sw <= 0 || sh <= 0) return null;

        // 5. Transform 適用 → transformed coords
        var transformedBbox = AutoCropCalculator.TransformRect(
            new PixelRect(sx, sy, sw, sh), sourceWidth, sourceHeight, transform);

        // 6. srcRectInTransformed (= cropped transformed area)
        var (cx, cy, cw, ch) = crop.ToPixelBbox(sourceWidth, sourceHeight);
        var srcRectInTransformed = AutoCropCalculator.TransformRect(
            new PixelRect(cx, cy, cw, ch), sourceWidth, sourceHeight, transform);
        if (srcRectInTransformed.Width <= 0 || srcRectInTransformed.Height <= 0) return null;

        // 7. dstRect (cell 内の描画矩形) = ScalingMode + Alignment + dest
        var dst = ComputeDstRectForFill(
            srcRectInTransformed.Width, srcRectInTransformed.Height,
            dest.X, dest.Y, dest.Width, dest.Height,
            scalingMode, alignment);
        if (dst.W <= 0 || dst.H <= 0) return null;

        // 8. transformedBbox ∩ srcRectInTransformed (可視部分)
        var visLeft = Math.Max((double)transformedBbox.X, srcRectInTransformed.X);
        var visTop = Math.Max((double)transformedBbox.Y, srcRectInTransformed.Y);
        var visRight = Math.Min((double)transformedBbox.X + transformedBbox.Width,
                                (double)srcRectInTransformed.X + srcRectInTransformed.Width);
        var visBottom = Math.Min((double)transformedBbox.Y + transformedBbox.Height,
                                 (double)srcRectInTransformed.Y + srcRectInTransformed.Height);
        if (visRight <= visLeft || visBottom <= visTop) return null;

        // 9. 線形写像 srcRect → dstRect で canvas 座標へ
        var localFx = (visLeft - srcRectInTransformed.X) / srcRectInTransformed.Width;
        var localFy = (visTop - srcRectInTransformed.Y) / srcRectInTransformed.Height;
        var localFw = (visRight - visLeft) / srcRectInTransformed.Width;
        var localFh = (visBottom - visTop) / srcRectInTransformed.Height;
        var canvasX = dst.X + localFx * dst.W;
        var canvasY = dst.Y + localFy * dst.H;
        var canvasW = localFw * dst.W;
        var canvasH = localFh * dst.H;

        // 10. cellRect で clip (PixelOffset で隣セルに飛び出した領域を除外)
        var clipL = Math.Max(canvasX, cellRect.X);
        var clipT = Math.Max(canvasY, cellRect.Y);
        var clipR = Math.Min(canvasX + canvasW, cellRect.X + cellRect.Width);
        var clipB = Math.Min(canvasY + canvasH, cellRect.Y + cellRect.Height);
        if (clipR <= clipL || clipB <= clipT) return null;

        return (clipL, clipT, clipR - clipL, clipB - clipT);
    }

    /// <summary>
    /// 新規 region 追加時に、 asset の初期 cell-local オフセット <c>(OffsetXPx, OffsetYPx)</c> を
    /// 「親側塗りと同じ位置に重なる」 値として逆算する。 親側塗りが計算不可 (visible 部分なし等)
    /// の場合は <c>null</c> を返し、 caller は (0, 0) フォールバックする。
    /// </summary>
    /// <remarks>
    /// renderer の <c>DrawRegionAsset</c> の流儀: asset の cell-local 左上 = (cellRect.X + OffsetXPx,
    /// cellRect.Y + OffsetYPx)。 これを親側塗りの canvas 左上 (visible bbox の左上、 clip 後) に合わせる。
    /// 整数 px に四捨五入してから返す。
    /// </remarks>
    public static (int OffsetX, int OffsetY)? ComputeOffsetMatchingParentFill(
        PixelSize canvasSize, int cols, int rows,
        IReadOnlyList<int> colWeights, IReadOnlyList<int> rowWeights,
        CellPosition position, OccupySize occupySize,
        int pixelOffsetX, int pixelOffsetY,
        ImageTransform transform,
        ScalingMode scalingMode, Alignment alignment,
        CropFraction? effectiveCrop,
        int sourceWidth, int sourceHeight,
        RegionRectFraction regionRect)
    {
        var fill = ComputeParentFillCanvasRect(
            canvasSize, cols, rows, colWeights, rowWeights,
            position, occupySize, pixelOffsetX, pixelOffsetY,
            transform, scalingMode, alignment, effectiveCrop,
            sourceWidth, sourceHeight, regionRect);
        if (fill is not { } r) return null;

        var cellRect = PlacementGeometry.ComputeDestRect(
            canvasSize, cols, rows, colWeights, rowWeights,
            position, occupySize, 0, 0);
        return (
            (int)Math.Round(r.X - cellRect.X),
            (int)Math.Round(r.Y - cellRect.Y));
    }

    /// <summary>
    /// <c>ScalingMode</c> + <c>Alignment</c> で dst 矩形 (cell-local pixel) を計算する。 親側塗り計算で
    /// 「cell 内のどこに source 全体が描画されるか」 を決める純粋関数。 cell クリップは行わない。
    /// </summary>
    private static (double X, double Y, double W, double H) ComputeDstRectForFill(
        double sw, double sh,
        double destX, double destY, double destW, double destH,
        ScalingMode mode, Alignment alignment)
    {
        if (mode == ScalingMode.Fill)
            return (destX, destY, destW, destH);

        var fitContain = Math.Min(destW / sw, destH / sh);
        var fitCover = Math.Max(destW / sw, destH / sh);
        var scale = mode switch
        {
            ScalingMode.None => 1.0,
            ScalingMode.UniformContain => fitContain,
            ScalingMode.UniformContainShrinkOnly => Math.Min(1.0, fitContain),
            ScalingMode.UniformContainEnlargeOnly => Math.Max(1.0, fitContain),
            ScalingMode.UniformCover => fitCover,
            _ => 1.0,
        };

        var (dx, dw) = ComputeAxis1D(sw, destX, destW, scale, alignment.X switch
        {
            AnchorX.Left => 0,
            AnchorX.Right => 2,
            _ => 1,
        });
        var (dy, dh) = ComputeAxis1D(sh, destY, destH, scale, alignment.Y switch
        {
            AnchorY.Top => 0,
            AnchorY.Bottom => 2,
            _ => 1,
        });
        return (dx, dy, dw, dh);
    }

    /// <summary>0=Start / 1=Center / 2=End。 <c>PlacementGeometry.ComputeAxisDst</c> と同仕様の 1 軸計算。</summary>
    private static (double DstStart, double DstLen) ComputeAxis1D(
        double srcSize, double dstStart, double dstSize, double scale, int anchor)
    {
        var drawSize = srcSize * scale;
        var pad = dstSize - drawSize;
        var dstOffset = anchor switch
        {
            0 => 0.0,        // Start
            2 => pad,        // End
            _ => pad / 2.0,  // Center
        };
        return (dstStart + dstOffset, drawSize);
    }
}
