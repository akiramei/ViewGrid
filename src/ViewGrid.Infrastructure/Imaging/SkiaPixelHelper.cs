using SkiaSharp;

namespace ViewGrid.Infrastructure.Imaging;

/// <summary>
/// SkiaSharp の <see cref="SKBitmap"/> のピクセルバイト列を「Rgba8888 + Unpremul」で
/// 安全に走査するためのヘルパ。<see cref="SKBitmap.Decode(string)"/> はプラットフォーム
/// ネイティブ形式（Windows では <see cref="SKColorType.Bgra8888"/>）で decode することが
/// あり、<see cref="SKBitmap.Bytes"/> を直接 RGBA 順として読むと R/B チャネルが swap される。
/// 本ヘルパは bitmap が「Rgba8888 + Unpremul」でなければその形式にコピーした新しい
/// bitmap を返し、<paramref name="ownsResult"/> で呼び出し側に dispose の必要性を通知する。
/// </summary>
internal static class SkiaPixelHelper
{
    /// <summary>
    /// <paramref name="source"/> が「Rgba8888 + Unpremul」ならその参照をそのまま返し
    /// （<paramref name="ownsResult"/> = false）、そうでなければ変換コピーを返す
    /// （<paramref name="ownsResult"/> = true、呼び出し側で dispose）。
    /// 変換失敗時は元の参照を返す（fallback、走査結果は誤る可能性あるが crash よりまし）。
    /// </summary>
    public static SKBitmap EnsureRgbaUnpremul(SKBitmap source, out bool ownsResult)
    {
        if (source.ColorType == SKColorType.Rgba8888 && source.AlphaType == SKAlphaType.Unpremul)
        {
            ownsResult = false;
            return source;
        }

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var copy = new SKBitmap(info);
        if (!source.CopyTo(copy))
        {
            copy.Dispose();
            ownsResult = false;
            return source;
        }
        ownsResult = true;
        return copy;
    }
}
