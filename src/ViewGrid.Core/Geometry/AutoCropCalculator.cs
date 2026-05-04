using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Geometry;

/// <summary>
/// 単色余白の自動トリミング bbox 計算（純粋関数）。
/// <para>
/// 画像の外周（上・下・左・右）から内側へ走査し、対象色から
/// <see cref="AutoCropSettings.Threshold"/> 以上離れたピクセルが現れるまで
/// 進める。残った内側矩形が「画像本来の絵柄領域」として扱われる。
/// </para>
/// <para>
/// 入力は RGBA8888 のバイト列（行ごとに <paramref name="rowStride"/> バイト、
/// 各ピクセルは R, G, B, A の 4 バイト）。SkiaSharp の <c>SKBitmap.Bytes</c> /
/// <c>SKBitmap.RowBytes</c> パターンと整合する。
/// </para>
/// </summary>
public static class AutoCropCalculator
{
    /// <summary>
    /// 外周走査で内側 bbox を計算する。
    /// <para>
    /// 結果が画像全体と同一（クロップ無効）の場合は <c>(0, 0, width, height)</c> を返す。
    /// 全画素が対象色だった場合（実質「全部余白」）も同様に画像全体を返す
    /// — 上位で no-op として扱える。負の幅/高さを返すことはない。
    /// </para>
    /// </summary>
    public static PixelRect Compute(
        ReadOnlySpan<byte> pixelsRgba8888,
        int width,
        int height,
        int rowStride,
        AutoCropSettings settings)
    {
        if (width <= 0 || height <= 0)
            return new PixelRect(0, 0, 0, 0);
        if (rowStride < width * 4)
            throw new ArgumentOutOfRangeException(nameof(rowStride),
                $"rowStride ({rowStride}) は width*4 ({width * 4}) 以上である必要があります。");
        if (pixelsRgba8888.Length < rowStride * height)
            throw new ArgumentException(
                $"pixels バッファ長が不足しています: {pixelsRgba8888.Length} < {rowStride * height}",
                nameof(pixelsRgba8888));

        var ta = (byte)((settings.TargetColorArgb >> 24) & 0xFF);
        var tr = (byte)((settings.TargetColorArgb >> 16) & 0xFF);
        var tg = (byte)((settings.TargetColorArgb >> 8) & 0xFF);
        var tb = (byte)(settings.TargetColorArgb & 0xFF);
        var thr = settings.Threshold;
        // α=0 ターゲット時は α のみで判定（透明プリセット用）。
        var alphaOnly = ta == 0;

        // 上から走査: 行 y で「異色ピクセル」が 1 つでもあれば停止 → top = y
        var top = 0;
        while (top < height && IsRowAllMatching(pixelsRgba8888, rowStride, top, 0, width, tr, tg, tb, ta, thr, alphaOnly))
            top++;

        if (top >= height)
            // 全画素が対象色 → 全領域を返す（呼び出し側で no-op 扱い）
            return new PixelRect(0, 0, width, height);

        // 下から走査
        var bottom = height - 1;
        while (bottom > top && IsRowAllMatching(pixelsRgba8888, rowStride, bottom, 0, width, tr, tg, tb, ta, thr, alphaOnly))
            bottom--;

        // 左から走査（top..bottom の縦範囲のみ見る）
        var left = 0;
        while (left < width && IsColumnAllMatching(pixelsRgba8888, rowStride, left, top, bottom + 1, tr, tg, tb, ta, thr, alphaOnly))
            left++;

        // 右から走査
        var right = width - 1;
        while (right > left && IsColumnAllMatching(pixelsRgba8888, rowStride, right, top, bottom + 1, tr, tg, tb, ta, thr, alphaOnly))
            right--;

        return new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// 走査結果の bbox を回転（90/180/270 度）と Flip に追従させる。
    /// 「回転前の原画像」で走査した bbox を「回転・反転後のビットマップ」上の
    /// bbox に変換するために使う。純粋関数。
    /// </summary>
    public static PixelRect TransformRect(
        PixelRect rect,
        int sourceWidth,
        int sourceHeight,
        ImageTransform transform)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return rect;

        // Flip → Rotate の順で適用（renderer の ApplyTransform と一致）
        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;

        if (transform.FlipX)
            x = sourceWidth - x - w;
        if (transform.FlipY)
            y = sourceHeight - y - h;

        // 回転後の幅・高さ
        return transform.Rotation switch
        {
            Rotation.None => new PixelRect(x, y, w, h),
            Rotation.Cw90 => new PixelRect(sourceHeight - y - h, x, h, w),
            Rotation.Cw180 => new PixelRect(sourceWidth - x - w, sourceHeight - y - h, w, h),
            Rotation.Cw270 => new PixelRect(y, sourceWidth - x - w, h, w),
            _ => new PixelRect(x, y, w, h),
        };
    }

    private static bool IsRowAllMatching(
        ReadOnlySpan<byte> pixels, int rowStride, int y, int x0, int x1,
        byte tr, byte tg, byte tb, byte ta, byte thr, bool alphaOnly)
    {
        var rowStart = y * rowStride;
        for (var x = x0; x < x1; x++)
        {
            var p = rowStart + x * 4;
            if (!Matches(pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3], tr, tg, tb, ta, thr, alphaOnly))
                return false;
        }
        return true;
    }

    private static bool IsColumnAllMatching(
        ReadOnlySpan<byte> pixels, int rowStride, int x, int y0, int y1,
        byte tr, byte tg, byte tb, byte ta, byte thr, bool alphaOnly)
    {
        for (var y = y0; y < y1; y++)
        {
            var p = y * rowStride + x * 4;
            if (!Matches(pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3], tr, tg, tb, ta, thr, alphaOnly))
                return false;
        }
        return true;
    }

    private static bool Matches(
        byte r, byte g, byte b, byte a,
        byte tr, byte tg, byte tb, byte ta, byte thr, bool alphaOnly)
    {
        if (alphaOnly)
            return a <= thr;
        var dr = r > tr ? r - tr : tr - r;
        var dg = g > tg ? g - tg : tg - g;
        var db = b > tb ? b - tb : tb - b;
        var da = a > ta ? a - ta : ta - a;
        var max = dr;
        if (dg > max) max = dg;
        if (db > max) max = db;
        if (da > max) max = da;
        return max <= thr;
    }
}
