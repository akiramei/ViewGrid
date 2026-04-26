using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using SkiaSharp;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Services;

internal sealed class SkiaImageProber : IImageProber
{
    public Task<ErrorOr<ImageProbe>> ProbeAsync(Stream stream, CancellationToken ct = default)
    {
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return Task.FromResult<ErrorOr<ImageProbe>>(
                Error.Validation("Image.Unreadable", "画像として解釈できませんでした。"));

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            return Task.FromResult<ErrorOr<ImageProbe>>(
                Error.Validation("Image.InvalidDimensions", "画像サイズが不正です。"));

        var mimeType = MapMimeType(codec.EncodedFormat);
        var probe = new ImageProbe(new PixelSize(info.Width, info.Height), mimeType);
        return Task.FromResult<ErrorOr<ImageProbe>>(probe);
    }

    private static string MapMimeType(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => "image/png",
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Gif => "image/gif",
        SKEncodedImageFormat.Webp => "image/webp",
        SKEncodedImageFormat.Bmp => "image/bmp",
        SKEncodedImageFormat.Heif => "image/heif",
        SKEncodedImageFormat.Avif => "image/avif",
        _ => "application/octet-stream",
    };

    public static string ExtensionFor(string mimeType) => mimeType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/heif" => ".heif",
        "image/avif" => ".avif",
        _ => ".bin",
    };
}
