using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドの配置一覧を集約し、PNG バイト列を合成して返す。
/// プレビュー表示・ファイル出力の両方で利用される共通ステージ。
/// </summary>
public sealed class RenderGridUseCase(
    IGridCanvasRepository gridRepository,
    IGridPlacementRepository placementRepository,
    IImageCopyRepository copyRepository,
    IImageAssetRepository assetRepository,
    IImageStorage imageStorage,
    IGridImageRenderer renderer)
{
    public async Task<ErrorOr<byte[]>> ExecuteAsync(
        Guid gridId,
        TrimMode trimMode = TrimMode.None,
        CancellationToken ct = default)
    {
        var grid = await gridRepository.FindByIdAsync(gridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {gridId} が見つかりません。");

        var placements = await placementRepository.FindByGridIdAsync(gridId, ct);

        var items = new List<PlacementRenderItem>(placements.Count);
        foreach (var placement in placements)
        {
            var copy = await copyRepository.FindByIdAsync(placement.CopyId, ct);
            if (copy is null)
                return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {placement.CopyId} が見つかりません。");

            var asset = await assetRepository.FindByIdAsync(copy.AssetId, ct);
            if (asset is null)
                return Error.NotFound("ImageAsset.NotFound", $"ImageAsset {copy.AssetId} が見つかりません。");

            var absolutePath = imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            items.Add(new PlacementRenderItem(placement, copy, absolutePath));
        }

        return await renderer.RenderPngAsync(grid, items, trimMode, ct);
    }
}
