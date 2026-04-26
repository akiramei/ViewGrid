using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using SkiaSharp;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Infrastructure.Imaging;

/// <summary>
/// SkiaSharp で配置済み画像をピクセル精度で合成し PNG バイト列を返す。
/// </summary>
internal sealed class SkiaGridImageRenderer : IGridImageRenderer
{
    public Task<ErrorOr<byte[]>> RenderPngAsync(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(items);

        return Task.Run<ErrorOr<byte[]>>(() => Render(grid, items, ct), ct);
    }

    private static ErrorOr<byte[]> Render(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        CancellationToken ct)
    {
        var info = new SKImageInfo(
            grid.CanvasSize.Width, grid.CanvasSize.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        if (surface is null)
            return Error.Failure("Render.SurfaceFailed", "出力サーフェスの作成に失敗しました。");

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { IsAntialias = true };
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        foreach (var item in items.OrderBy(i => i.Placement.PlacementOrder))
        {
            ct.ThrowIfCancellationRequested();
            var error = DrawOne(canvas, grid, item, sampling, paint);
            if (error is not null)
                return error.Value;
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
            return Error.Failure("Render.EncodeFailed", "PNG エンコードに失敗しました。");

        return encoded.ToArray();
    }

    private static Error? DrawOne(
        SKCanvas canvas,
        GridCanvas grid,
        PlacementRenderItem item,
        SKSamplingOptions sampling,
        SKPaint paint)
    {
        if (!File.Exists(item.SourceImageAbsolutePath))
            return Error.NotFound(
                "Render.SourceMissing",
                $"アセットファイルが見つかりません: {item.SourceImageAbsolutePath}");

        using var stream = File.OpenRead(item.SourceImageAbsolutePath);
        using var decoded = SKBitmap.Decode(stream);
        if (decoded is null)
            return Error.Validation(
                "Render.DecodeFailed",
                $"画像のデコードに失敗しました: {item.SourceImageAbsolutePath}");

        using var transformed = ApplyTransform(decoded, item.Copy.Transform);
        using var transformedImage = SKImage.FromBitmap(transformed);
        if (transformedImage is null)
            return Error.Failure(
                "Render.ImageFromBitmapFailed",
                $"画像変換に失敗しました: {item.SourceImageAbsolutePath}");

        // セル領域（PixelOffset 適用前）。クリップ範囲として使う。
        var cellRect = PlacementGeometry.ComputeDestRect(
            grid.CanvasSize,
            grid.GridCols,
            grid.GridRows,
            grid.ColWeights,
            grid.RowWeights,
            item.Placement.Position,
            item.Copy.OccupySize,
            pixelOffsetX: 0,
            pixelOffsetY: 0);

        // 描画 dest（PixelOffset 適用後）。セル外に動いた部分は ClipRect で切られる。
        var dest = PlacementGeometry.ComputeDestRect(
            grid.CanvasSize,
            grid.GridCols,
            grid.GridRows,
            grid.ColWeights,
            grid.RowWeights,
            item.Placement.Position,
            item.Copy.OccupySize,
            item.Placement.PixelOffsetX,
            item.Placement.PixelOffsetY);

        if (dest.Width <= 0 || dest.Height <= 0)
            return null;

        var (srcRect, dstRect) = ComputeSrcDstRects(
            transformed.Width, transformed.Height,
            dest, item.Copy);

        if (srcRect.Width <= 0 || srcRect.Height <= 0 ||
            dstRect.Width <= 0 || dstRect.Height <= 0)
            return null;

        // セル境界でクリップ。PixelOffset で隣セルに侵入しないことを保証。
        canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height));
            canvas.DrawImage(transformedImage, srcRect, dstRect, sampling, paint);
        }
        finally
        {
            canvas.Restore();
        }
        return null;
    }

    /// <summary>
    /// ImageTransform（回転・反転）を焼き込んだ新しいビットマップを作る。
    /// 反転 → 回転 の順（Phase 3-D の TransformGroup と一致）。
    /// </summary>
    private static SKBitmap ApplyTransform(SKBitmap source, ImageTransform transform)
    {
        var rotateSwap = transform.Rotation is Rotation.Cw90 or Rotation.Cw270;
        var dstW = rotateSwap ? source.Height : source.Width;
        var dstH = rotateSwap ? source.Width : source.Height;

        var dst = new SKBitmap(dstW, dstH, source.ColorType, source.AlphaType);
        try
        {
            using var canvas = new SKCanvas(dst);
            canvas.Clear(SKColors.Transparent);

            canvas.Translate(dstW / 2f, dstH / 2f);
            canvas.RotateDegrees((int)transform.Rotation);
            canvas.Scale(transform.FlipX ? -1f : 1f, transform.FlipY ? -1f : 1f);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            canvas.DrawBitmap(source, 0, 0);
            return dst;
        }
        catch
        {
            dst.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 変換後画像（sw, sh）と配置先矩形 dest、コピーの特性から、
    /// DrawBitmap に渡す src/dst の SKRect を計算する。<see cref="ScalingMode"/> で挙動を切り替え、
    /// Uniform 系は軸ごとに「収まる」「はみ出す」で独立に判定する。
    /// </summary>
    private static (SKRect Src, SKRect Dst) ComputeSrcDstRects(
        int sw, int sh,
        PixelRect dest,
        ImageCopy copy)
    {
        // Fill: 縦横独立スケールで dst を完全充填（aspect 破壊）
        if (copy.ScalingMode == ScalingMode.Fill)
        {
            return (
                SKRect.Create(0f, 0f, sw, sh),
                SKRect.Create(dest.X, dest.Y, dest.Width, dest.Height));
        }

        var fitContain = Math.Min((double)dest.Width / sw, (double)dest.Height / sh);
        var fitCover = Math.Max((double)dest.Width / sw, (double)dest.Height / sh);

        var scale = copy.ScalingMode switch
        {
            ScalingMode.None => 1.0,
            ScalingMode.UniformContain => fitContain,
            ScalingMode.UniformContainShrinkOnly => Math.Min(1.0, fitContain),
            ScalingMode.UniformContainEnlargeOnly => Math.Max(1.0, fitContain),
            ScalingMode.UniformCover => fitCover,
            _ => 1.0,
        };

        // ScalingMode.None は位置決めも TrimmingAnchor を使う（はみ出し軸の挙動と統一して
        // 「収まる軸でも TrimmingAnchor で寄せる」ようにする）。それ以外は Alignment。
        var useTrimForPosition = copy.ScalingMode == ScalingMode.None;
        var positionAnchorX = useTrimForPosition ? ToAnchor1D(copy.TrimmingAnchor.X) : ToAnchor1D(copy.Alignment.X);
        var positionAnchorY = useTrimForPosition ? ToAnchor1D(copy.TrimmingAnchor.Y) : ToAnchor1D(copy.Alignment.Y);
        var trimAnchorX = ToAnchor1D(copy.TrimmingAnchor.X);
        var trimAnchorY = ToAnchor1D(copy.TrimmingAnchor.Y);

        var (srcX, srcW, dstX, dstW) = ComputeAxis(sw, dest.X, dest.Width, scale, positionAnchorX, trimAnchorX);
        var (srcY, srcH, dstY, dstH) = ComputeAxis(sh, dest.Y, dest.Height, scale, positionAnchorY, trimAnchorY);

        return (
            SKRect.Create((float)srcX, (float)srcY, (float)srcW, (float)srcH),
            SKRect.Create((float)dstX, (float)dstY, (float)dstW, (float)dstH));
    }

    private enum Anchor1D { Start, Center, End }

    private static Anchor1D ToAnchor1D(AnchorX a) => a switch
    {
        AnchorX.Left => Anchor1D.Start,
        AnchorX.Right => Anchor1D.End,
        _ => Anchor1D.Center,
    };

    private static Anchor1D ToAnchor1D(AnchorY a) => a switch
    {
        AnchorY.Top => Anchor1D.Start,
        AnchorY.Bottom => Anchor1D.End,
        _ => Anchor1D.Center,
    };

    private static (double SrcStart, double SrcLen, double DstStart, double DstLen) ComputeAxis(
        double srcSize,
        double dstStart,
        double dstSize,
        double scale,
        Anchor1D positionAnchor,
        Anchor1D trimAnchor)
    {
        var drawSize = srcSize * scale;
        if (drawSize <= dstSize)
        {
            var pad = dstSize - drawSize;
            var dstOffset = positionAnchor switch
            {
                Anchor1D.Start => 0.0,
                Anchor1D.End => pad,
                _ => pad / 2.0,
            };
            return (0.0, srcSize, dstStart + dstOffset, drawSize);
        }
        else
        {
            var visibleSrc = dstSize / scale;
            var pad = srcSize - visibleSrc;
            var srcOffset = trimAnchor switch
            {
                Anchor1D.Start => 0.0,
                Anchor1D.End => pad,
                _ => pad / 2.0,
            };
            return (srcOffset, visibleSrc, dstStart, dstSize);
        }
    }
}
