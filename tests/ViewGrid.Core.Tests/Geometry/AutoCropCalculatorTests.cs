using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// <see cref="AutoCropCalculator"/> の境界値テスト。
/// 外周走査の bbox 計算と、回転・反転下での bbox 変換を検証する。
/// </summary>
public sealed class AutoCropCalculatorTests
{
    private const int Stride = 4;

    /// <summary>
    /// 全画素が対象色（白）→ 画像全体を返す（クロップ無効と同義）。
    /// </summary>
    [Fact]
    public void Compute_All_Target_Color_Returns_Full_Rect()
    {
        var pixels = MakePixels(4, 4, Color(255, 255, 255, 255));

        var rect = AutoCropCalculator.Compute(pixels, 4, 4, 4 * Stride, AutoCropSettings.White);

        rect.Should().Be(new PixelRect(0, 0, 4, 4));
    }

    /// <summary>
    /// 中央 1px だけ異色 → 上下左右が 1px ずつ手前で停止する bbox（中央 1px のみ）。
    /// </summary>
    [Fact]
    public void Compute_Center_Pixel_Different_Stops_Around_It()
    {
        // 5×5 の白背景、中央 (2,2) のみ黒
        var pixels = MakePixels(5, 5, Color(255, 255, 255, 255));
        SetPixel(pixels, 5 * Stride, 2, 2, Color(0, 0, 0, 255));

        var rect = AutoCropCalculator.Compute(pixels, 5, 5, 5 * Stride, AutoCropSettings.White);

        rect.Should().Be(new PixelRect(2, 2, 1, 1));
    }

    /// <summary>
    /// 上 1 行と左 1 列だけ白、それ以外が黒 → 上 1px / 左 1px で停止し、bbox は (1,1,4,4)。
    /// </summary>
    [Fact]
    public void Compute_Asymmetric_Margins_Top_And_Left_Only()
    {
        // 5×5、(0,*) 行と (*,0) 列が白、それ以外は黒
        var pixels = MakePixels(5, 5, Color(0, 0, 0, 255));
        for (var x = 0; x < 5; x++)
            SetPixel(pixels, 5 * Stride, x, 0, Color(255, 255, 255, 255));
        for (var y = 0; y < 5; y++)
            SetPixel(pixels, 5 * Stride, 0, y, Color(255, 255, 255, 255));

        var rect = AutoCropCalculator.Compute(pixels, 5, 5, 5 * Stride, AutoCropSettings.White);

        rect.Should().Be(new PixelRect(1, 1, 4, 4));
    }

    /// <summary>
    /// 閾値 0 では完全一致のみ余白扱い。閾値 8 では微小差も余白扱い → bbox が異なる。
    /// </summary>
    [Fact]
    public void Compute_Threshold_Affects_Result()
    {
        // 5×5、外周は (250,250,250) で内側は (255,255,255)。閾値 8 なら全画素マッチ → 全領域。
        // 閾値 0 なら外周は不一致なので 1px 内側でも止まらない（実は 250 は対象色 255 と異なる）。
        var pixels = MakePixels(5, 5, Color(250, 250, 250, 255));
        for (var y = 1; y < 4; y++)
            for (var x = 1; x < 4; x++)
                SetPixel(pixels, 5 * Stride, x, y, Color(255, 255, 255, 255));

        // Threshold=0: 外周は対象色(255)と一致しない → 1 行も剥がせず top=0 で停止
        var strict = AutoCropCalculator.Compute(pixels, 5, 5, 5 * Stride, new AutoCropSettings(0xFFFFFFFFu, 0));
        strict.Should().Be(new PixelRect(0, 0, 5, 5));

        // Threshold=8: 全画素一致 → 全領域
        var lenient = AutoCropCalculator.Compute(pixels, 5, 5, 5 * Stride, new AutoCropSettings(0xFFFFFFFFu, 8));
        lenient.Should().Be(new PixelRect(0, 0, 5, 5));
    }

    /// <summary>
    /// 透明プリセット（α=0）は RGB を無視して α のみで判定する。
    /// </summary>
    [Fact]
    public void Compute_Transparent_Preset_Only_Looks_At_Alpha()
    {
        // 4×4、外周は α=0（任意 RGB）、中央 2×2 が α=255 の有色
        var pixels = MakePixels(4, 4, Color(123, 45, 67, 0));
        for (var y = 1; y < 3; y++)
            for (var x = 1; x < 3; x++)
                SetPixel(pixels, 4 * Stride, x, y, Color(0, 0, 0, 255));

        var rect = AutoCropCalculator.Compute(pixels, 4, 4, 4 * Stride, AutoCropSettings.Transparent);

        rect.Should().Be(new PixelRect(1, 1, 2, 2));
    }

    /// <summary>
    /// 1×1 画像（極端ケース）で例外なく結果を返す。
    /// </summary>
    [Fact]
    public void Compute_OnePixel_Image_Does_Not_Throw()
    {
        var pixels = MakePixels(1, 1, Color(255, 255, 255, 255));
        var rect = AutoCropCalculator.Compute(pixels, 1, 1, 1 * Stride, AutoCropSettings.White);
        rect.Should().Be(new PixelRect(0, 0, 1, 1));
    }

    /// <summary>
    /// rowStride にパディング込みのバッファでも bbox が正しく計算できる
    /// （SkiaSharp の <c>RowBytes</c> が <c>width*4</c> より大きいケースを想定）。
    /// </summary>
    [Fact]
    public void Compute_With_Padded_Stride_Works_Correctly()
    {
        // 3×3、stride=16（width*4=12、+4 のパディング）
        var stride = 16;
        var pixels = new byte[stride * 3];
        for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
            {
                var p = y * stride + x * Stride;
                var (r, g, b, a) = (x == 1 && y == 1) ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255);
                pixels[p] = r;
                pixels[p + 1] = g;
                pixels[p + 2] = b;
                pixels[p + 3] = a;
            }

        var rect = AutoCropCalculator.Compute(pixels, 3, 3, stride, AutoCropSettings.White);

        rect.Should().Be(new PixelRect(1, 1, 1, 1));
    }

    /// <summary>
    /// 不正な rowStride（width*4 未満）は例外を投げる。
    /// </summary>
    [Fact]
    public void Compute_Invalid_Stride_Throws()
    {
        var pixels = new byte[16];
        var act = () => AutoCropCalculator.Compute(pixels, 4, 4, 4, AutoCropSettings.White);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----------- TransformRect -----------

    /// <summary>
    /// 回転なし・反転なしなら矩形は不変。
    /// </summary>
    [Fact]
    public void TransformRect_Identity_Returns_Same_Rect()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        var result = AutoCropCalculator.TransformRect(rect, 100, 200, ImageTransform.Identity);
        result.Should().Be(rect);
    }

    /// <summary>
    /// Cw90: (x, y, w, h) on (sW, sH) → (sH - y - h, x, h, w) on (sH, sW)。
    /// 100×200 の画像内 (10, 20, 30, 40) は、回転後 200×100 上で (140, 10, 40, 30)。
    /// </summary>
    [Fact]
    public void TransformRect_Cw90_Rotates_Coordinates()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        var transform = new ImageTransform(Rotation.Cw90, false, false);
        var result = AutoCropCalculator.TransformRect(rect, 100, 200, transform);
        result.Should().Be(new PixelRect(200 - 20 - 40, 10, 40, 30));
    }

    /// <summary>
    /// Cw180: 中心点反転。100×200 上の (10, 20, 30, 40) → (60, 140, 30, 40)。
    /// </summary>
    [Fact]
    public void TransformRect_Cw180_Flips_Both_Axes()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        var transform = new ImageTransform(Rotation.Cw180, false, false);
        var result = AutoCropCalculator.TransformRect(rect, 100, 200, transform);
        result.Should().Be(new PixelRect(100 - 10 - 30, 200 - 20 - 40, 30, 40));
    }

    /// <summary>
    /// Cw270: (x, y, w, h) on (sW, sH) → (y, sW - x - w, h, w)。
    /// 100×200 上の (10, 20, 30, 40) → (20, 60, 40, 30) on (200, 100)。
    /// </summary>
    [Fact]
    public void TransformRect_Cw270_Rotates_Coordinates()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        var transform = new ImageTransform(Rotation.Cw270, false, false);
        var result = AutoCropCalculator.TransformRect(rect, 100, 200, transform);
        result.Should().Be(new PixelRect(20, 100 - 10 - 30, 40, 30));
    }

    /// <summary>
    /// FlipX のみ: x が反転、y/w/h は不変。
    /// </summary>
    [Fact]
    public void TransformRect_FlipX_Only()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        var transform = new ImageTransform(Rotation.None, true, false);
        var result = AutoCropCalculator.TransformRect(rect, 100, 200, transform);
        result.Should().Be(new PixelRect(100 - 10 - 30, 20, 30, 40));
    }

    /// <summary>
    /// FlipY のみ: y が反転、x/w/h は不変。
    /// </summary>
    [Fact]
    public void TransformRect_FlipY_Only()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        var transform = new ImageTransform(Rotation.None, false, true);
        var result = AutoCropCalculator.TransformRect(rect, 100, 200, transform);
        result.Should().Be(new PixelRect(10, 200 - 20 - 40, 30, 40));
    }

    // ----------- Helpers -----------

    private static byte[] MakePixels(int width, int height, (byte R, byte G, byte B, byte A) color)
    {
        var stride = width * Stride;
        var buf = new byte[stride * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                SetPixel(buf, stride, x, y, color);
        return buf;
    }

    private static void SetPixel(byte[] buf, int stride, int x, int y, (byte R, byte G, byte B, byte A) color)
    {
        var p = y * stride + x * Stride;
        buf[p] = color.R;
        buf[p + 1] = color.G;
        buf[p + 2] = color.B;
        buf[p + 3] = color.A;
    }

    private static (byte R, byte G, byte B, byte A) Color(byte r, byte g, byte b, byte a) => (r, g, b, a);

    private static uint ArgbOf(byte a, byte r, byte g, byte b)
        => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
}
