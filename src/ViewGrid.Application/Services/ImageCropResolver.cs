using System.Threading;
using System.Threading.Tasks;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
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

        // ManualCrop 優先（排他）
        if (copy.ManualCrop is { } manual)
        {
            var cf = CropFraction.From(manual);
            return cf.IsFull() ? null : cf;
        }

        // AutoCrop（既存パス、cache 経由）
        if (copy.AutoCrop is { } settings)
        {
            var path = imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            var fraction = await autoCropResolver.ResolveAsync(asset.Id, path, settings, ct);
            return fraction is { } f ? CropFraction.From(f) : null;
        }

        return null;
    }
}
