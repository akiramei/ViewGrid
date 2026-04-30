using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Imaging;

/// <summary>
/// <see cref="IImageColorPicker"/> の SkiaSharp 実装。<see cref="SKBitmap.Decode(string)"/>
/// で画像を読み込み <see cref="SKBitmap.GetPixel"/> でピクセル色を返す。
/// </summary>
internal sealed class SkiaImageColorPicker : IImageColorPicker
{
    public Task<uint?> PickColorAsync(string imageAbsolutePath, int x, int y, CancellationToken ct = default)
    {
        return Task.Run<uint?>(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(imageAbsolutePath))
                return null;

            using var bitmap = SKBitmap.Decode(imageAbsolutePath);
            if (bitmap is null)
                return null;
            if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
                return null;

            var color = bitmap.GetPixel(x, y);
            return ((uint)color.Alpha << 24)
                | ((uint)color.Red << 16)
                | ((uint)color.Green << 8)
                | color.Blue;
        }, ct);
    }
}
