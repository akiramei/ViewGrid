using Microsoft.Extensions.Logging;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 全アセットのサムネを 1 件ずつ再生成する。 <see cref="IThumbnailService.GenerateAsync"/> は
/// 既存ファイルがあれば早期リターンする冪等仕様のため、 設定 (`ThumbnailMaxEdgePixels`) を
/// 変更しても既存サムネは置き換わらない。 本 UseCase は <see cref="IThumbnailService.RegenerateAsync"/>
/// を呼び出すことで「削除してから生成」 する経路を全件分通す。
/// <para>
/// 並列化はしない (Skia デコードは CPU bound、 並列化のメリットは薄く、 I/O 衝突回避のため
/// 順次実行が安全)。 進捗は <see cref="IProgress{T}"/> でレポートされ、 UI 側は別スレッドで
/// 受け取ることを想定する (Dispatcher.UIThread.Post 等で UI スレッドへマーシャル)。
/// </para>
/// </summary>
public sealed partial class RegenerateThumbnailsUseCase(
    IImageAssetRepository assetRepository,
    IThumbnailService thumbnailService,
    ILogger<RegenerateThumbnailsUseCase> logger)
{
    private readonly IImageAssetRepository _assetRepository = assetRepository;
    private readonly IThumbnailService _thumbnailService = thumbnailService;
    private readonly ILogger<RegenerateThumbnailsUseCase> _logger = logger;

    public async Task<RegenerateThumbnailsResult> ExecuteAsync(
        IProgress<ThumbnailRegenProgress>? progress,
        CancellationToken ct = default)
    {
        // 事前キャンセル状態 (cts.Cancel 後の呼び出し) では FindAllAsync 内で OperationCanceled
        // が投げられるため、 先頭で IsCancellationRequested を見て早期リターンする
        if (ct.IsCancellationRequested)
            return new RegenerateThumbnailsResult(0, 0, 0, 0, Cancelled: true);

        var assets = await _assetRepository.FindAllAsync(ct);
        var total = assets.Count;
        var successful = 0;
        var skipped = 0;
        var failed = 0;
        var cancelled = false;

        // 開始時の進捗 (Total を View に伝えるため初回 Report は必須)
        progress?.Report(new ThumbnailRegenProgress(
            Completed: 0, Total: total, CurrentAssetName: string.Empty,
            Successful: 0, Skipped: 0, Failed: 0));

        for (var i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var asset = assets[i];
            var result = await _thumbnailService.RegenerateAsync(
                asset.StoredRelativePath, asset.FileHash, ct);

            if (result.IsError)
            {
                // SourceMissing は「元画像が消えた」 ケース → Skipped、 それ以外は Failed
                var isSourceMissing = result.Errors.Any(e => e.Code == "Thumbnail.SourceMissing");
                if (isSourceMissing)
                {
                    skipped++;
                    LogAssetSkipped(_logger, asset.OriginalFilename ?? string.Empty, asset.FileHash);
                }
                else
                {
                    failed++;
                    LogAssetFailed(_logger, asset.OriginalFilename ?? string.Empty, asset.FileHash,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                successful++;
            }

            progress?.Report(new ThumbnailRegenProgress(
                Completed: i + 1, Total: total, CurrentAssetName: asset.OriginalFilename ?? string.Empty,
                Successful: successful, Skipped: skipped, Failed: failed));
        }

        return new RegenerateThumbnailsResult(
            Total: total, Successful: successful, Skipped: skipped, Failed: failed,
            Cancelled: cancelled);
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "サムネ再生成: 元画像不在のためスキップ filename={Filename} hash={Hash}")]
    private static partial void LogAssetSkipped(ILogger logger, string filename, string hash);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "サムネ再生成: 失敗 filename={Filename} hash={Hash} error={Error}")]
    private static partial void LogAssetFailed(ILogger logger, string filename, string hash, string error);
}
