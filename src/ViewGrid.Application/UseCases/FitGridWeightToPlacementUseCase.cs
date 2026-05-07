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
    IImageCropResolver cropResolver,
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

        // セル矩形（PixelOffset=0）。
        // OccupySize は配置固有の特性として placement から取得する。
        var cellRect = PlacementGeometry.ComputeDestRect(
            grid.CanvasSize, grid.GridCols, grid.GridRows,
            grid.ColWeights, grid.RowWeights,
            placement.Position, placement.OccupySize, 0, 0);

        // 画像の 未クリップ描画矩形 (cell 境界クリップ前)。 画像が cell より大きい場合は
        // 両側に負の pad が出るため、 拡大方向のフィット判定に使う。
        var (drawX, drawY, drawW, drawH) = PlacementGeometry.ComputeImageDrawBounds(
            grid.CanvasSize, grid.GridCols, grid.GridRows,
            grid.ColWeights, grid.RowWeights,
            placement.Position, placement.OccupySize,
            effectiveSourceW, effectiveSourceH, copy,
            placement.PixelOffsetX, placement.PixelOffsetY);

        if (axis == FitAxis.Column)
        {
            return await FitColumnAsync(
                grid, placement, copy, cellRect, drawX, drawW,
                effectiveSourceW, effectiveSourceH, ct);
        }
        else
        {
            return await FitRowAsync(
                grid, placement, copy, cellRect, drawY, drawH,
                effectiveSourceW, effectiveSourceH, ct);
        }
    }

    private async Task<ErrorOr<Success>> FitColumnAsync(
        GridCanvas grid, GridPlacement placement, ImageCopy copy,
        PixelRect cellRect, double drawX, double drawW,
        int effectiveSourceW, int effectiveSourceH,
        CancellationToken ct)
    {
        var unclippedLeftPad = drawX - cellRect.X;
        var unclippedRightPad = (cellRect.X + cellRect.Width) - (drawX + drawW);

        long leftPad, inner, rightPad;
        if (unclippedLeftPad < 0 && unclippedRightPad < 0)
        {
            // 純粋な overflow (両側で画像が cell をはみ出す) = 拡大モード。
            // cell を image 描画幅に合わせて広げ、 隣接列から重みを引く (signed pad)。
            leftPad = (long)Math.Round(unclippedLeftPad);
            inner = (long)Math.Round(drawW);
            rightPad = (long)Math.Round(unclippedRightPad);
        }
        else
        {
            // 通常 / 片側 overflow (PixelOffset 等): 旧来の visible-rect ベース縮小モード。
            // 既存の Cover+PixelOffset 挙動 (visible 部に合わせて cell 縮小) を維持。
            var renderedRect = PlacementGeometry.ComputeRenderedRect(
                grid.CanvasSize, grid.GridCols, grid.GridRows,
                grid.ColWeights, grid.RowWeights,
                placement.Position, placement.OccupySize,
                effectiveSourceW, effectiveSourceH, copy,
                placement.PixelOffsetX, placement.PixelOffsetY);
            leftPad = renderedRect.X - cellRect.X;
            inner = renderedRect.Width;
            rightPad = (cellRect.X + cellRect.Width) - (renderedRect.X + renderedRect.Width);
            if (leftPad < 0) leftPad = 0;
            if (rightPad < 0) rightPad = 0;
        }

        LogFitDiagColumn(logger,
            copy.ScalingMode, effectiveSourceW, effectiveSourceH,
            cellRect.X, cellRect.Width,
            (int)Math.Round(drawX), (int)Math.Round(drawW),
            (int)leftPad, (int)inner, (int)rightPad);

        if (inner <= 0) return Result.Success;
        if (leftPad == 0 && rightPad == 0) return Result.Success;

        var newColWeights = WeightRedistributor.FitToOccupant(
            grid.ColWeights,
            placement.Position.X, placement.OccupySize.Width,
            leftPad, inner, rightPad,
            grid.ColLocked.IsDefaultOrEmpty ? null : grid.ColLocked);

        var result = await updateWeights.ExecuteAsync(grid.Id, newColWeights, null, ct);
        return result.IsError ? result.Errors : Result.Success;
    }

    private async Task<ErrorOr<Success>> FitRowAsync(
        GridCanvas grid, GridPlacement placement, ImageCopy copy,
        PixelRect cellRect, double drawY, double drawH,
        int effectiveSourceW, int effectiveSourceH,
        CancellationToken ct)
    {
        var unclippedTopPad = drawY - cellRect.Y;
        var unclippedBottomPad = (cellRect.Y + cellRect.Height) - (drawY + drawH);

        long topPad, inner, bottomPad;
        if (unclippedTopPad < 0 && unclippedBottomPad < 0)
        {
            topPad = (long)Math.Round(unclippedTopPad);
            inner = (long)Math.Round(drawH);
            bottomPad = (long)Math.Round(unclippedBottomPad);
        }
        else
        {
            var renderedRect = PlacementGeometry.ComputeRenderedRect(
                grid.CanvasSize, grid.GridCols, grid.GridRows,
                grid.ColWeights, grid.RowWeights,
                placement.Position, placement.OccupySize,
                effectiveSourceW, effectiveSourceH, copy,
                placement.PixelOffsetX, placement.PixelOffsetY);
            topPad = renderedRect.Y - cellRect.Y;
            inner = renderedRect.Height;
            bottomPad = (cellRect.Y + cellRect.Height) - (renderedRect.Y + renderedRect.Height);
            if (topPad < 0) topPad = 0;
            if (bottomPad < 0) bottomPad = 0;
        }

        LogFitDiagRow(logger,
            copy.ScalingMode, effectiveSourceW, effectiveSourceH,
            cellRect.Y, cellRect.Height,
            (int)Math.Round(drawY), (int)Math.Round(drawH),
            (int)topPad, (int)inner, (int)bottomPad);

        if (inner <= 0) return Result.Success;
        if (topPad == 0 && bottomPad == 0) return Result.Success;

        var newRowWeights = WeightRedistributor.FitToOccupant(
            grid.RowWeights,
            placement.Position.Y, placement.OccupySize.Height,
            topPad, inner, bottomPad,
            grid.RowLocked.IsDefaultOrEmpty ? null : grid.RowLocked);

        var result = await updateWeights.ExecuteAsync(grid.Id, null, newRowWeights, ct);
        return result.IsError ? result.Errors : Result.Success;
    }

    /// <summary>
    /// 「クロップ適用後の論理画像サイズ（原画像座標系、回転前）」を返す。
    /// ManualCrop または AutoCrop が有効ならその比率で縮めたサイズを、どちらも無効なら
    /// <see cref="ImageAsset.Size"/> をそのまま使う（<see cref="IImageCropResolver"/> 経由で
    /// 優先順位を解決）。
    /// </summary>
    private async Task<(int Width, int Height)> ResolveEffectiveSourceSizeAsync(
        ImageAsset asset, ImageCopy copy, CancellationToken ct)
    {
        var fraction = await cropResolver.ResolveAsync(copy, asset, ct);
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
