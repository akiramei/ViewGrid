using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.Services;

/// <summary>
/// <see cref="IImageCropResolver"/> の標準実装。<see cref="ImageCopy.ManualCrop"/> を最優先、
/// 次に <see cref="ImageCopy.AutoCrop"/>、どちらも null なら null を返す。
/// AutoCrop 経路は <see cref="IAutoCropBboxResolver"/>（cache 付き）に委譲する。
/// </summary>
public sealed class ImageCropResolver(
    IAutoCropBboxResolver autoCropResolver,
    IImageStorage imageStorage) : IImageCropResolver
{
    public async Task<CropFraction?> ResolveAsync(ImageCopy copy, ImageAsset asset, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(copy);
        System.ArgumentNullException.ThrowIfNull(asset);

        // AutoCrop 走査（I/O）は ManualCrop が無いときだけ起こす（排他・短絡を維持）。
        // 走査結果を含む優先判定 (ManualCrop>AutoCrop>null, full→null) は CropFraction.ResolveEffective に委譲。
        AutoCropFraction? autoFraction = null;
        if (copy.ManualCrop is null && copy.AutoCrop is { } settings)
        {
            var path = imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            autoFraction = await autoCropResolver.ResolveAsync(asset.Id, path, settings, ct);
        }

        return CropFraction.ResolveEffective(copy.ManualCrop, autoFraction);
    }
}
