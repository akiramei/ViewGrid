using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// ローカルファイルを取り込み、<see cref="ImageAsset"/> と既定の <see cref="ImageCopy"/> を作成する。
/// ハッシュ一致の既存アセットがあれば、新規作成せずそれを返す（重複排除）。
/// </summary>
public sealed partial class ImportImageUseCase(
    IImageHasher hasher,
    IImageProber prober,
    IImageStorage storage,
    IThumbnailService thumbnailService,
    IImageAssetRepository assetRepository,
    IImageCopyRepository copyRepository,
    ILogger<ImportImageUseCase> logger)
{
    private readonly IImageHasher _hasher = hasher;
    private readonly IImageProber _prober = prober;
    private readonly IImageStorage _storage = storage;
    private readonly IThumbnailService _thumbnailService = thumbnailService;
    private readonly IImageAssetRepository _assetRepository = assetRepository;
    private readonly IImageCopyRepository _copyRepository = copyRepository;
    private readonly ILogger<ImportImageUseCase> _logger = logger;

    public async Task<ErrorOr<ImportImageResult>> ExecuteAsync(ImportImageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.SourcePath))
            return Error.NotFound("ImportImage.FileNotFound", $"ファイルが見つかりません: {request.SourcePath}");

        var fileInfo = new FileInfo(request.SourcePath);

        string fileHash;
        await using (var hashStream = File.OpenRead(request.SourcePath))
        {
            fileHash = await _hasher.ComputeHashAsync(hashStream, ct);
        }

        var existing = await _assetRepository.FindByHashAsync(fileHash, ct);
        if (existing is not null)
        {
            var defaultCopy = await FindOrCreateDefaultCopyAsync(existing.Id, ct);
            return defaultCopy.Match<ErrorOr<ImportImageResult>>(
                copy => new ImportImageResult(existing, copy, WasDuplicate: true),
                errors => errors);
        }

        ImageProbe probe;
        await using (var probeStream = File.OpenRead(request.SourcePath))
        {
            var probeResult = await _prober.ProbeAsync(probeStream, ct);
            if (probeResult.IsError)
                return probeResult.Errors;
            probe = probeResult.Value;
        }

        var extension = ExtensionFor(probe.MimeType, fileInfo.Extension);
        var relativePath = _storage.BuildRelativePath(fileHash, extension);

        if (!_storage.Exists(relativePath))
        {
            await using var source = File.OpenRead(request.SourcePath);
            await _storage.SaveAsync(source, relativePath, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var asset = new ImageAsset
        {
            Id = Guid.NewGuid(),
            SourceType = request.SourceType,
            OriginalFilename = fileInfo.Name,
            StoredRelativePath = relativePath,
            Size = probe.Size,
            FileHash = fileHash,
            FileSizeBytes = fileInfo.Length,
            MimeType = probe.MimeType,
            CreatedAt = now,
        };

        var addAsset = await _assetRepository.AddAsync(asset, ct);
        if (addAsset.IsError)
        {
            _storage.Delete(relativePath);
            return addAsset.Errors;
        }

        var thumbnail = await _thumbnailService.GenerateAsync(relativePath, fileHash, ct);
        if (thumbnail.IsError)
        {
            // サムネイル失敗は致命ではない：ログのみで続行する。
            LogThumbnailGenerationFailed(_logger, fileHash, string.Join(", ", thumbnail.Errors));
        }

        var copyResult = await CreateDefaultCopyAsync(asset.Id, now, ct);
        if (copyResult.IsError)
            return copyResult.Errors;

        return new ImportImageResult(asset, copyResult.Value, WasDuplicate: false);
    }

    private async Task<ErrorOr<ImageCopy>> FindOrCreateDefaultCopyAsync(Guid assetId, CancellationToken ct)
    {
        var copies = await _copyRepository.FindByAssetIdAsync(assetId, ct);
        if (copies.Count > 0)
            return copies[0];

        return await CreateDefaultCopyAsync(assetId, DateTimeOffset.UtcNow, ct);
    }

    private async Task<ErrorOr<ImageCopy>> CreateDefaultCopyAsync(Guid assetId, DateTimeOffset now, CancellationToken ct)
    {
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            CopyName = null,
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return await _copyRepository.AddAsync(copy, ct);
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "サムネイル生成に失敗しました hash={Hash}: {Errors}")]
    private static partial void LogThumbnailGenerationFailed(ILogger logger, string hash, string errors);

    private static string ExtensionFor(string mimeType, string? originalExtension)
    {
        var mapped = mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/heif" => ".heif",
            "image/avif" => ".avif",
            _ => null,
        };

        if (mapped is not null)
            return mapped;

        return !string.IsNullOrWhiteSpace(originalExtension) ? originalExtension.ToLowerInvariant() : ".bin";
    }
}
