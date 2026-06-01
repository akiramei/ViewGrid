// === F-P10 blind generation artifact (NOT compiled; under experiments/) ===
// crop-resolver-spec.md のみから独立生成器が生成した ImageCropResolver。既存実装は非開示で生成。
// 実装との発散: ctor 引数に null チェックを追加 (実装は primary ctor で null チェックなし)。
//   precedence / 短絡 / I/O 条件 / null 両義 / 前提ガード (ResolveAsync の ThrowIfNull) は振る舞い一致。
using System;
using System.Threading;
using System.Threading.Tasks;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.Services;

/// <summary>
/// ImageCopy の ManualCrop / AutoCrop 設定から実効クロップ比率 (<see cref="CropFraction"/>) を解決する。
/// precedence: ManualCrop 排他優先 → AutoCrop → null (IR-04)。
/// </summary>
public sealed class ImageCropResolver : IImageCropResolver
{
    private readonly IAutoCropBboxResolver _autoCropResolver;
    private readonly IImageStorage _imageStorage;

    public ImageCropResolver(IAutoCropBboxResolver autoCropResolver, IImageStorage imageStorage)
    {
        _autoCropResolver = autoCropResolver ?? throw new ArgumentNullException(nameof(autoCropResolver));
        _imageStorage = imageStorage ?? throw new ArgumentNullException(nameof(imageStorage));
    }

    public async Task<CropFraction?> ResolveAsync(ImageCopy copy, ImageAsset asset, CancellationToken ct = default)
    {
        // 1. 前提ガード (前提違反は例外。結果チャネルとは別)。
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(asset);

        // 2-a. ManualCrop 排他優先。設定時点で AutoCrop は一切参照しない (短絡)。
        if (copy.ManualCrop is { } manual)
        {
            CropFraction fraction = CropFraction.From(manual);
            return fraction.IsFull() ? null : fraction;
        }

        // 2-b. AutoCrop 経路。この経路に入った時のみ I/O を起こす。
        if (copy.AutoCrop is { } settings)
        {
            string path = _imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            AutoCropFraction? resolved = await _autoCropResolver
                .ResolveAsync(asset.Id, path, settings, ct)
                .ConfigureAwait(false);

            return resolved is { } auto ? CropFraction.From(auto) : null;
        }

        // 2-c. both-off → null (AutoCrop I/O なし)。
        return null;
    }
}
