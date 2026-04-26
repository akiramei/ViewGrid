using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using SkiaSharp;
using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Services;

internal sealed class SkiaThumbnailService(StorageOptions options, IImageStorage storage) : IThumbnailService
{
    private readonly StorageOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IImageStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    public int MaxEdgePixels => 256;

    public async Task<ErrorOr<string>> GenerateAsync(
        string assetRelativePath,
        string fileHash,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetRelativePath);
        ArgumentException.ThrowIfNullOrEmpty(fileHash);

        var thumbRelative = BuildRelativePath(fileHash);
        var thumbAbsolute = ResolveAbsolutePath(thumbRelative);

        if (File.Exists(thumbAbsolute))
            return thumbRelative;

        var sourceAbsolute = _storage.ResolveAbsolutePath(assetRelativePath);
        if (!File.Exists(sourceAbsolute))
            return Error.NotFound("Thumbnail.SourceMissing", $"アセットが見つかりません: {assetRelativePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(thumbAbsolute)!);

        try
        {
            using var input = File.OpenRead(sourceAbsolute);
            using var bitmap = SKBitmap.Decode(input);
            if (bitmap is null)
                return Error.Validation("Thumbnail.DecodeFailed", "画像のデコードに失敗しました。");

            var (targetWidth, targetHeight) = CalculateThumbnailSize(bitmap.Width, bitmap.Height);
            var samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

            using var resized = bitmap.Resize(
                new SKImageInfo(targetWidth, targetHeight),
                samplingOptions);

            if (resized is null)
                return Error.Failure("Thumbnail.ResizeFailed", "サムネイルのリサイズに失敗しました。");

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality: 80);

            await using var output = File.Create(thumbAbsolute);
            encoded.SaveTo(output);
            await output.FlushAsync(ct);

            return thumbRelative;
        }
        catch (IOException ex)
        {
            return Error.Failure("Thumbnail.IoError", ex.Message);
        }
    }

    public string? TryResolveAbsolutePath(string fileHash)
    {
        if (string.IsNullOrEmpty(fileHash))
            return null;

        var absolute = ResolveAbsolutePath(BuildRelativePath(fileHash));
        return File.Exists(absolute) ? absolute : null;
    }

    private string BuildRelativePath(string fileHash)
    {
        var shard = fileHash[..2];
        return $"{_options.ThumbnailsSubdirectory}/{shard}/{fileHash}.webp";
    }

    private string ResolveAbsolutePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_options.DataDirectory, normalized);
    }

    private (int Width, int Height) CalculateThumbnailSize(int sourceWidth, int sourceHeight)
    {
        var longEdge = Math.Max(sourceWidth, sourceHeight);
        if (longEdge <= MaxEdgePixels)
            return (sourceWidth, sourceHeight);

        var scale = (double)MaxEdgePixels / longEdge;
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return (width, height);
    }
}
