using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// アセットと論理コピー（DB 上は cascade）、実体ファイルを削除する。
/// </summary>
public sealed class DeleteImageAssetUseCase(
    IImageAssetRepository assetRepository,
    IImageStorage storage,
    IThumbnailService thumbnailService)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(Guid assetId, CancellationToken ct = default)
    {
        var asset = await assetRepository.FindByIdAsync(assetId, ct);
        if (asset is null)
            return Error.NotFound("ImageAsset.NotFound", $"ImageAsset {assetId} が見つかりません。");

        var deleteResult = await assetRepository.DeleteAsync(assetId, ct);
        if (deleteResult.IsError)
            return deleteResult.Errors;

        // DB から除去できたら実体ファイルとサムネイルを削除する。
        storage.Delete(asset.StoredRelativePath);
        var thumbnailAbsolute = thumbnailService.TryResolveAbsolutePath(asset.FileHash);
        if (thumbnailAbsolute is not null && System.IO.File.Exists(thumbnailAbsolute))
            System.IO.File.Delete(thumbnailAbsolute);

        return Result.Success;
    }
}
