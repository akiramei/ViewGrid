using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 既存の <see cref="ImageAsset"/> から新しい論理コピーを作成する。
/// </summary>
public sealed class CreateLogicalCopyUseCase(
    IImageAssetRepository assetRepository,
    IImageCopyRepository copyRepository)
{
    public async Task<ErrorOr<ImageCopy>> ExecuteAsync(
        Guid assetId,
        string? copyName = null,
        ImageTransform? transform = null,
        CancellationToken ct = default)
    {
        var asset = await assetRepository.FindByIdAsync(assetId, ct);
        if (asset is null)
            return Error.NotFound("ImageAsset.NotFound", $"ImageAsset {assetId} が見つかりません。");

        var now = DateTimeOffset.UtcNow;
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            CopyName = copyName,
            Transform = transform ?? ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return await copyRepository.AddAsync(copy, ct);
    }
}
