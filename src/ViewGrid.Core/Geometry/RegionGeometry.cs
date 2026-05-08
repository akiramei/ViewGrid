using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Geometry;

/// <summary>
/// <see cref="ProtectedRegion"/> と effective Crop の交差結果。
/// Phase 1 の renderer は <see cref="SourceRect"/> (元画像 0–1) で 「Region が画像内のどこに
/// あるか」 を、 <see cref="CropLocalRect"/> (Crop 内 0–1) で 「cell-bounded image (= Crop 後の
/// 画像) のどこを overlay として切り出し / 親側に白塗りするか」 を決める。
/// </summary>
public readonly record struct EffectiveRegion(
    RegionRectFraction SourceRect,
    RegionRectFraction CropLocalRect);

/// <summary>
/// PhotoBoard の最終キャンバス上での overlay 配置。
/// 中心位置 (<see cref="CanvasCenterX"/> / <see cref="CanvasCenterY"/>) は PhotoBoard の親変換
/// (offset + rotation around pivot) を適用した後の sub-pixel 座標。
/// サイズ (<see cref="WidthPx"/> / <see cref="HeightPx"/>) は cell-local pixel size と同一で、
/// overlay は canvas 水平で axis-aligned 描画される (PhotoBoard rotation を打ち消す KeepUpright 仕様)。
/// </summary>
public readonly record struct OverlayPlacement(
    double CanvasCenterX,
    double CanvasCenterY,
    double WidthPx,
    double HeightPx);

/// <summary>
/// <see cref="ProtectedRegion"/> の幾何計算を純粋関数で提供する。
/// renderer (<c>SkiaGridImageRenderer</c>) は本クラスの 2 関数を組み合わせて、
/// 「Region をどこに白塗りし、 どこに overlay を描くか」 を決定する。
/// </summary>
/// <remarks>
/// <para>処理パイプラインの位置付け:</para>
/// <list type="number">
///   <item>renderer が ImageCropResolver で effective Crop を解決</item>
///   <item><see cref="Intersect"/> で Region × Crop の交差を計算 (null = 描画なし)</item>
///   <item>renderer が cell-bounded image を生成 (Transform / ScalingMode / Alignment 適用)</item>
///   <item><see cref="EffectiveRegion.CropLocalRect"/> で 「cell image のどこを overlay slice に
///     使うか」 と 「親側で白塗りする領域」 を決める</item>
///   <item>PhotoBoardLayout.Compute で各 placement の <see cref="PhotoBoardItem"/> を取得</item>
///   <item><see cref="ComputeOverlayPlacement"/> で overlay の最終キャンバス上の位置を確定</item>
///   <item>renderer は cell image に白塗り → 最終キャンバスに親画像合成 → overlay を canvas 水平で描画</item>
/// </list>
/// <para>Transform (90° 回転等) と ScalingMode は cell-bounded image の生成時点で焼き込まれて
/// いる前提で、 本クラスでは cell-local 座標から canvas 座標への変換だけを担う。</para>
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
    /// <paramref name="item"/> の親変換 (BaseRect + offset + rotation around pivot) を適用し、
    /// cell-local 0–1 で表現された <paramref name="cellLocalRegion"/> が最終キャンバス上で
    /// どこに配置されるかを計算する。
    /// </summary>
    /// <param name="item">対象 placement の PhotoBoard 描画パラメータ。</param>
    /// <param name="cellLocalRegion">cell-bounded image (= BaseRect 内画像) における Region の
    /// 位置 (0–1)。 通常は <see cref="EffectiveRegion.CropLocalRect"/> を渡す。</param>
    /// <remarks>
    /// 「KeepUpright」 仕様: 戻り値の中心位置は PhotoBoard rotation を含む親変換後の位置だが、
    /// overlay は canvas 水平で描画する想定なので Width / Height は cell-local pixel size と
    /// 同一の axis-aligned サイズを返す。 ImageCopy.Transform は cell-bounded image の中身に
    /// すでに焼き込まれている前提で、 本関数では Transform を直接扱わない。
    /// </remarks>
    public static OverlayPlacement ComputeOverlayPlacement(
        PhotoBoardItem item,
        RegionRectFraction cellLocalRegion)
    {
        ArgumentNullException.ThrowIfNull(item);

        var cellW = (double)item.BaseRect.Width;
        var cellH = (double)item.BaseRect.Height;

        // 1. cell-local pixel 座標 (cell-bounded image 内)
        var localCenterX = (cellLocalRegion.X + cellLocalRegion.Width / 2.0) * cellW;
        var localCenterY = (cellLocalRegion.Y + cellLocalRegion.Height / 2.0) * cellH;
        var widthPx = cellLocalRegion.Width * cellW;
        var heightPx = cellLocalRegion.Height * cellH;

        // 2. parent canvas 座標 (BaseRect + cell-local + PhotoBoard offset、 rotation 適用前)
        var parentX = item.BaseRect.X + localCenterX + item.OffsetX;
        var parentY = item.BaseRect.Y + localCenterY + item.OffsetY;

        // 3. rotation pivot (cell center + pivot offset、 offset 適用後の canvas 座標)
        var pivotX = item.BaseRect.X + cellW / 2.0 + item.OffsetX + item.RotationPivotOffsetX;
        var pivotY = item.BaseRect.Y + cellH / 2.0 + item.OffsetY + item.RotationPivotOffsetY;

        // 4. rotation 適用 (pivot を中心に回転)
        var rad = item.RotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = parentX - pivotX;
        var dy = parentY - pivotY;
        var rotatedX = pivotX + dx * cos - dy * sin;
        var rotatedY = pivotY + dx * sin + dy * cos;

        return new OverlayPlacement(rotatedX, rotatedY, widthPx, heightPx);
    }
}
