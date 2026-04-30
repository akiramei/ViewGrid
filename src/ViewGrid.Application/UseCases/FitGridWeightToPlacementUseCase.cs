using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドフィットの対象軸。<see cref="Column"/> は列幅、<see cref="Row"/> は行高を、
/// 指定 placement の実描画矩形に合わせて縮める。
/// </summary>
public enum FitAxis
{
    Column,
    Row,
}

/// <summary>
/// 指定 placement が占有するセルの列幅または行高を、画像の実描画矩形にフィットさせる。
/// 余白は隣接列/行に分配する（左余白 → 左隣、右余白 → 右隣、上下も同様）。
/// 端列/端行で隣接がない側の余白は破棄され、画像が端に張り付く挙動になる。
/// </summary>
public sealed partial class FitGridWeightToPlacementUseCase(
    IGridCanvasRepository gridRepository,
    IGridPlacementRepository placementRepository,
    IImageCopyRepository copyRepository,
    IImageAssetRepository assetRepository,
    IImageStorage imageStorage,
    IAutoCropBboxResolver autoCropResolver,
    UpdateGridWeightsUseCase updateWeights,
    ILogger<FitGridWeightToPlacementUseCase> logger)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(
        Guid placementId,
        FitAxis axis,
        CancellationToken ct = default)
    {
        var placement = await placementRepository.FindByIdAsync(placementId, ct);
        if (placement is null)
            return Error.NotFound("Placement.NotFound", $"GridPlacement {placementId} が見つかりません。");

        var copy = await copyRepository.FindByIdAsync(placement.CopyId, ct);
        if (copy is null)
            return Error.NotFound("Copy.NotFound", $"ImageCopy {placement.CopyId} が見つかりません。");

        var asset = await assetRepository.FindByIdAsync(copy.AssetId, ct);
        if (asset is null)
            return Error.NotFound("Asset.NotFound", $"ImageAsset {copy.AssetId} が見つかりません。");

        var grid = await gridRepository.FindByIdAsync(placement.GridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {placement.GridId} が見つかりません。");

        // AutoCrop 適用後の「論理画像サイズ（原画像座標系、回転前）」を求める。
        // レンダラと同じ effective size を使うことで、フィット計算の dst 矩形がレンダラの
        // 描画結果と一致する（AutoCrop でアスペクト比が変わっても余白計算が正しい）。
        var (effectiveSourceW, effectiveSourceH) = await ResolveEffectiveSourceSizeAsync(asset, copy, ct);

        // セル矩形（PixelOffset=0）と実描画矩形（PixelOffset 込み + クリップ済み）
        var cellRect = PlacementGeometry.ComputeDestRect(
            grid.CanvasSize, grid.GridCols, grid.GridRows,
            grid.ColWeights, grid.RowWeights,
            placement.Position, copy.OccupySize, 0, 0);

        var renderedRect = PlacementGeometry.ComputeRenderedRect(
            grid.CanvasSize, grid.GridCols, grid.GridRows,
            grid.ColWeights, grid.RowWeights,
            placement.Position,
            effectiveSourceW, effectiveSourceH, copy,
            placement.PixelOffsetX, placement.PixelOffsetY);

        if (axis == FitAxis.Column)
        {
            var leftPad = renderedRect.X - cellRect.X;
            var inner = renderedRect.Width;
            var rightPad = (cellRect.X + cellRect.Width) - (renderedRect.X + renderedRect.Width);

            LogFitDiagColumn(logger,
                copy.ScalingMode, effectiveSourceW, effectiveSourceH,
                cellRect.X, cellRect.Width,
                renderedRect.X, renderedRect.Width,
                leftPad, inner, rightPad);

            if (inner <= 0) return Result.Success;
            if (leftPad < 0) leftPad = 0;
            if (rightPad < 0) rightPad = 0;
            if (leftPad == 0 && rightPad == 0) return Result.Success;

            var newColWeights = WeightRedistributor.FitToOccupant(
                grid.ColWeights,
                placement.Position.X, copy.OccupySize.Width,
                leftPad, inner, rightPad,
                grid.ColLocked.IsDefaultOrEmpty ? null : grid.ColLocked);

            var result = await updateWeights.ExecuteAsync(grid.Id, newColWeights, null, ct);
            return result.IsError ? result.Errors : Result.Success;
        }
        else
        {
            var topPad = renderedRect.Y - cellRect.Y;
            var inner = renderedRect.Height;
            var bottomPad = (cellRect.Y + cellRect.Height) - (renderedRect.Y + renderedRect.Height);

            LogFitDiagRow(logger,
                copy.ScalingMode, effectiveSourceW, effectiveSourceH,
                cellRect.Y, cellRect.Height,
                renderedRect.Y, renderedRect.Height,
                topPad, inner, bottomPad);

            if (inner <= 0) return Result.Success;
            if (topPad < 0) topPad = 0;
            if (bottomPad < 0) bottomPad = 0;
            if (topPad == 0 && bottomPad == 0) return Result.Success;

            var newRowWeights = WeightRedistributor.FitToOccupant(
                grid.RowWeights,
                placement.Position.Y, copy.OccupySize.Height,
                topPad, inner, bottomPad,
                grid.RowLocked.IsDefaultOrEmpty ? null : grid.RowLocked);

            var result = await updateWeights.ExecuteAsync(grid.Id, null, newRowWeights, ct);
            return result.IsError ? result.Errors : Result.Success;
        }
    }

    /// <summary>
    /// 「AutoCrop 適用後の論理画像サイズ（原画像座標系、回転前）」を返す。
    /// AutoCrop OFF または fraction 取得失敗時は <see cref="ImageAsset.Size"/> をそのまま使う。
    /// </summary>
    private async Task<(int Width, int Height)> ResolveEffectiveSourceSizeAsync(
        ImageAsset asset, ImageCopy copy, CancellationToken ct)
    {
        if (copy.AutoCrop is not { } settings)
            return (asset.Size.Width, asset.Size.Height);

        var absolutePath = imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
        var fraction = await autoCropResolver.ResolveAsync(asset.Id, absolutePath, settings, ct);
        if (fraction is not { } f || f.IsFull())
            return (asset.Size.Width, asset.Size.Height);

        var (_, _, w, h) = f.ToPixelBbox(asset.Size.Width, asset.Size.Height);
        if (w <= 0 || h <= 0)
            return (asset.Size.Width, asset.Size.Height);
        return (w, h);
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information,
        Message = "FitColumn: scaling={ScalingMode} src={SrcW}x{SrcH} cell.x={CellX} cell.w={CellW} rendered.x={RenderedX} rendered.w={RenderedW} leftPad={LeftPad} inner={Inner} rightPad={RightPad}")]
    private static partial void LogFitDiagColumn(ILogger logger,
        ScalingMode scalingMode, int srcW, int srcH,
        int cellX, int cellW, int renderedX, int renderedW,
        int leftPad, int inner, int rightPad);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information,
        Message = "FitRow: scaling={ScalingMode} src={SrcW}x{SrcH} cell.y={CellY} cell.h={CellH} rendered.y={RenderedY} rendered.h={RenderedH} topPad={TopPad} inner={Inner} bottomPad={BottomPad}")]
    private static partial void LogFitDiagRow(ILogger logger,
        ScalingMode scalingMode, int srcW, int srcH,
        int cellY, int cellH, int renderedY, int renderedH,
        int topPad, int inner, int bottomPad);
}
