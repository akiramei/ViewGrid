using System;
using System.Collections.Generic;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.UseCases;

/// <summary>
/// 出力画像上の整数矩形。Width/Height は正であることを保証する。
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// セル座標から出力ピクセル矩形への純粋な変換。隣接セル境界の整数丸めを揃え、
/// スキマや重なりが出ないよう、各境界を <c>canvas * sum(weights[..i]) / total</c> として
/// 個別に計算する。重み配列が省略された場合は均等配置（旧来の挙動）。
/// </summary>
public static class PlacementGeometry
{
    /// <summary>
    /// 均等配置版（旧シグネチャ互換）。テスト用途や重みが不要な呼び出し元向け。
    /// </summary>
    public static PixelRect ComputeDestRect(
        PixelSize canvas,
        int gridCols,
        int gridRows,
        CellPosition position,
        OccupySize occupy,
        int pixelOffsetX = 0,
        int pixelOffsetY = 0)
        => ComputeDestRect(
            canvas, gridCols, gridRows,
            colWeights: null, rowWeights: null,
            position, occupy, pixelOffsetX, pixelOffsetY);

    /// <summary>
    /// 重み配列版。<paramref name="colWeights"/> / <paramref name="rowWeights"/> が
    /// <c>null</c> または要素数不一致なら均等扱い。
    /// </summary>
    public static PixelRect ComputeDestRect(
        PixelSize canvas,
        int gridCols,
        int gridRows,
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CellPosition position,
        OccupySize occupy,
        int pixelOffsetX = 0,
        int pixelOffsetY = 0)
    {
        if (gridCols <= 0) throw new ArgumentOutOfRangeException(nameof(gridCols), gridCols, "gridCols must be positive.");
        if (gridRows <= 0) throw new ArgumentOutOfRangeException(nameof(gridRows), gridRows, "gridRows must be positive.");
        if (position.X + occupy.Width > gridCols) throw new ArgumentOutOfRangeException(nameof(occupy), "Placement exceeds grid columns.");
        if (position.Y + occupy.Height > gridRows) throw new ArgumentOutOfRangeException(nameof(occupy), "Placement exceeds grid rows.");

        // 累積重み配列: prefix[i] = sum(weights[0..i]).
        // weights が null/不一致なら全 1（均等）として扱う。
        var colPrefix = BuildPrefixSums(colWeights, gridCols);
        var rowPrefix = BuildPrefixSums(rowWeights, gridRows);
        var colTotal = colPrefix[gridCols];
        var rowTotal = rowPrefix[gridRows];

        var x0 = (long)canvas.Width * colPrefix[position.X] / colTotal;
        var x1 = (long)canvas.Width * colPrefix[position.X + occupy.Width] / colTotal;
        var y0 = (long)canvas.Height * rowPrefix[position.Y] / rowTotal;
        var y1 = (long)canvas.Height * rowPrefix[position.Y + occupy.Height] / rowTotal;

        var x = checked((int)x0) + pixelOffsetX;
        var y = checked((int)y0) + pixelOffsetY;
        var w = checked((int)(x1 - x0));
        var h = checked((int)(y1 - y0));

        return new PixelRect(x, y, w, h);
    }

    /// <summary>
    /// 与えられた重み配列の累積和（要素数 = <paramref name="count"/> + 1）を返す。
    /// 各重みが正でない場合は 1 として扱う（実用上の安全策）。
    /// </summary>
    /// <summary>
    /// 占有セル群のバウンディングボックスを計算する。<see cref="TrimMode.OccupiedCells"/>
    /// での出力切り出しに使う純粋関数。各 placement の <see cref="ComputeDestRect"/>
    /// （PixelOffset=0）の和集合を取り、結果をキャンバス境界にクランプして返す。
    /// </summary>
    /// <param name="placements">配置の (位置, 占有サイズ) 列。空なら 0 矩形。</param>
    /// <returns>占有矩形。配置がない場合は <c>(0,0,0,0)</c>。</returns>
    public static PixelRect ComputeOccupiedBoundingBox(
        PixelSize canvas,
        int gridCols,
        int gridRows,
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        IReadOnlyList<(CellPosition Position, OccupySize Occupy)> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count == 0)
            return new PixelRect(0, 0, 0, 0);

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        foreach (var (pos, occ) in placements)
        {
            var rect = ComputeDestRect(
                canvas, gridCols, gridRows, colWeights, rowWeights,
                pos, occ, pixelOffsetX: 0, pixelOffsetY: 0);
            if (rect.X < minX) minX = rect.X;
            if (rect.Y < minY) minY = rect.Y;
            if (rect.X + rect.Width > maxX) maxX = rect.X + rect.Width;
            if (rect.Y + rect.Height > maxY) maxY = rect.Y + rect.Height;
        }

        // キャンバス境界にクランプ（PixelOffset 等で外に出ることはないが安全策）
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(canvas.Width, maxX);
        maxY = Math.Min(canvas.Height, maxY);

        if (maxX <= minX || maxY <= minY)
            return new PixelRect(0, 0, 0, 0);

        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static long[] BuildPrefixSums(IReadOnlyList<int>? weights, int count)
    {
        var prefix = new long[count + 1];
        var useUniform = weights is null || weights.Count != count;
        long acc = 0;
        for (var i = 0; i < count; i++)
        {
            var w = useUniform ? 1 : weights![i];
            if (w <= 0) w = 1;
            acc += w;
            prefix[i + 1] = acc;
        }
        return prefix;
    }

    /// <summary>
    /// 指定 placement の <b>セル境界クリップ後の実描画矩形</b> をキャンバス座標で返す。
    /// セル矩形と画像描画矩形（ScalingMode + PixelOffset 反映後）の交差を取り、
    /// PixelOffset でセル外に出た部分はクリップされる。空の場合は Width/Height = 0。
    /// </summary>
    /// <remarks>
    /// 計算は <c>SkiaGridImageRenderer.ComputeSrcDstRects</c> と同じセマンティクスを再現するが、
    /// この関数は dst 矩形のみ返す純粋関数で、グリッドの行/列フィット計算（隣接列に余白を分配する）に使う。
    /// </remarks>
    public static PixelRect ComputeRenderedRect(
        PixelSize canvas,
        int gridCols,
        int gridRows,
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CellPosition position,
        OccupySize occupy,
        int sourceWidth,
        int sourceHeight,
        ImageCopy copy)
        => ComputeRenderedRect(
            canvas, gridCols, gridRows, colWeights, rowWeights,
            position, occupy, sourceWidth, sourceHeight, copy,
            pixelOffsetX: 0, pixelOffsetY: 0);

    /// <summary>
    /// PixelOffset を含めた版。OccupySize は配置単位の固有特性として呼び出し元から渡す。
    /// </summary>
    public static PixelRect ComputeRenderedRect(
        PixelSize canvas,
        int gridCols,
        int gridRows,
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CellPosition position,
        OccupySize occupy,
        int sourceWidth,
        int sourceHeight,
        ImageCopy copy,
        int pixelOffsetX,
        int pixelOffsetY)
    {
        ArgumentNullException.ThrowIfNull(copy);
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return new PixelRect(0, 0, 0, 0);

        var rotateSwap = copy.Transform.Rotation is Rotation.Cw90 or Rotation.Cw270;
        var sw = rotateSwap ? sourceHeight : sourceWidth;
        var sh = rotateSwap ? sourceWidth : sourceHeight;

        var cellRect = ComputeDestRect(
            canvas, gridCols, gridRows, colWeights, rowWeights,
            position, occupy, 0, 0);

        var destX = (double)cellRect.X + pixelOffsetX;
        var destY = (double)cellRect.Y + pixelOffsetY;
        var destW = (double)cellRect.Width;
        var destH = (double)cellRect.Height;

        return ComputeRenderedRectCore(cellRect, destX, destY, destW, destH, sw, sh, copy);
    }

    private static PixelRect ComputeRenderedRectCore(
        PixelRect cellRect,
        double destX, double destY, double destW, double destH,
        int sw, int sh,
        ImageCopy copy)
    {
        if (destW <= 0 || destH <= 0)
            return new PixelRect(cellRect.X, cellRect.Y, 0, 0);

        double dstX, dstY, dstW, dstH;

        if (copy.ScalingMode == ScalingMode.Fill)
        {
            dstX = destX;
            dstY = destY;
            dstW = destW;
            dstH = destH;
        }
        else
        {
            var fitContain = Math.Min(destW / sw, destH / sh);
            var fitCover = Math.Max(destW / sw, destH / sh);
            var scale = copy.ScalingMode switch
            {
                ScalingMode.None => 1.0,
                ScalingMode.UniformContain => fitContain,
                ScalingMode.UniformContainShrinkOnly => Math.Min(1.0, fitContain),
                ScalingMode.UniformContainEnlargeOnly => Math.Max(1.0, fitContain),
                ScalingMode.UniformCover => fitCover,
                _ => 1.0,
            };

            // 位置決め・トリミングを単一の Alignment アンカーで表現する
            // （CSS background-position と同じ意味論。画像 ≤ セルなら配置位置、
            //  画像 > セルならソースの可視範囲を同じアンカーで決める）。
            var anchorX = ToAnchor1D(copy.Alignment.X);
            var anchorY = ToAnchor1D(copy.Alignment.Y);

            (dstX, dstW) = ComputeAxisDst(sw, destX, destW, scale, anchorX);
            (dstY, dstH) = ComputeAxisDst(sh, destY, destH, scale, anchorY);
        }

        // セル境界でクリップ
        var l = Math.Max(dstX, cellRect.X);
        var t = Math.Max(dstY, cellRect.Y);
        var r = Math.Min(dstX + dstW, cellRect.X + cellRect.Width);
        var b = Math.Min(dstY + dstH, cellRect.Y + cellRect.Height);

        if (r <= l || b <= t)
            return new PixelRect(cellRect.X, cellRect.Y, 0, 0);

        return new PixelRect(
            checked((int)Math.Round(l)),
            checked((int)Math.Round(t)),
            checked((int)Math.Round(r - l)),
            checked((int)Math.Round(b - t)));
    }

    private enum Anchor1D { Start, Center, End }

    private static Anchor1D ToAnchor1D(AnchorX a) => a switch
    {
        AnchorX.Left => Anchor1D.Start,
        AnchorX.Right => Anchor1D.End,
        _ => Anchor1D.Center,
    };

    private static Anchor1D ToAnchor1D(AnchorY a) => a switch
    {
        AnchorY.Top => Anchor1D.Start,
        AnchorY.Bottom => Anchor1D.End,
        _ => Anchor1D.Center,
    };

    private static (double DstStart, double DstLen) ComputeAxisDst(
        double srcSize, double destStart, double destSize, double scale, Anchor1D anchor)
    {
        // <see cref="SkiaGridImageRenderer.ComputeAxis"/> と整合する仕様: 画像全体を scale 倍した
        // dst に anchor 位置で配置する。画像 > セル の場合 pad < 0 となり dst が cell を
        // 超えるが、後段の cellRect 交差で表示矩形は cell 内に収まる。
        var drawSize = srcSize * scale;
        var pad = destSize - drawSize;
        var offset = anchor switch
        {
            Anchor1D.Start => 0.0,
            Anchor1D.End => pad,
            _ => pad / 2.0,
        };
        return (destStart + offset, drawSize);
    }
}
