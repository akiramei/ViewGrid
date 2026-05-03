using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using SkiaSharp;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.Services;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Infrastructure.Imaging;

/// <summary>
/// SkiaSharp で配置済み画像をピクセル精度で合成し PNG バイト列を返す。
/// </summary>
internal sealed class SkiaGridImageRenderer : IGridImageRenderer
{
    private readonly AutoCropCache _autoCropCache;

    public SkiaGridImageRenderer(AutoCropCache autoCropCache)
    {
        _autoCropCache = autoCropCache;
    }

    public Task<ErrorOr<byte[]>> RenderPngAsync(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        RenderOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run<ErrorOr<byte[]>>(() => Render(grid, items, options, ct), ct);
    }

    private ErrorOr<byte[]> Render(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        RenderOptions options,
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
        return EncodeWithTrim(image, grid, items, options.TrimMode);
    }

    /// <summary>
    /// 描画ピクセル走査で「描画あり」とみなす α 閾値。アンチエイリアスや影で生じる
    /// 微小 α (1〜数程度) は視覚的に透明と区別がつかないため bbox から除外する。
    /// 8 (約 3%) は一般的な PNG 透過素材で「透明」とみなせる上限の経験値。
    /// </summary>
    private const byte DrawnPixelAlphaThreshold = 8;

    /// <summary>
    /// レンダリング結果の <see cref="SKImage"/> に <paramref name="trimMode"/> を適用して
    /// PNG バイト列に変換する。<see cref="TrimMode.None"/> は全面そのまま、
    /// <see cref="TrimMode.OccupiedCells"/> は占有セル bbox で切り出し、
    /// <see cref="TrimMode.DrawnPixels"/> は占有セル bbox 内をピクセル走査して
    /// α &gt;= 閾値の bbox で切り出し（占有セル外には決して拡張されない）。
    /// </summary>
    private ErrorOr<byte[]> EncodeWithTrim(
        SKImage image,
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        TrimMode trimMode)
    {
        SKRectI? cropRect = trimMode switch
        {
            TrimMode.OccupiedCells => ComputeOccupiedCellsRect(grid, items),
            // 走査範囲を占有セル bbox に絞ることで、(a) 想定外の α が占有セル外に漏れていても
            // bbox を膨らませない安全弁、(b) 走査ピクセル数の削減によるパフォーマンス向上、
            // の両方を得る。
            TrimMode.DrawnPixels => ComputeDrawnPixelsRect(
                image,
                ComputeOccupiedCellsRect(grid, items),
                ComputeRenderedGeometryRect(grid, items)),
            _ => null,
        };

        if (cropRect is null)
            return EncodePng(image);

        // 何も描画されていない / 計算不能の場合は 1×1 透過 PNG にフォールバック（ファイル破損を避ける）
        if (cropRect.Value.Width <= 0 || cropRect.Value.Height <= 0)
        {
            using var emptyInfo = new SKBitmap(1, 1);
            emptyInfo.Erase(SKColors.Transparent);
            using var emptyImage = SKImage.FromBitmap(emptyInfo);
            return EncodePng(emptyImage);
        }

        using var subset = image.Subset(cropRect.Value);
        if (subset is null)
            return Error.Failure("Render.SubsetFailed", "トリミング切り出しに失敗しました。");
        return EncodePng(subset);
    }

    private static ErrorOr<byte[]> EncodePng(SKImage image)
    {
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
            return Error.Failure("Render.EncodeFailed", "PNG エンコードに失敗しました。");
        return encoded.ToArray();
    }

    /// <summary>
    /// 占有セル群の bbox を <see cref="PlacementGeometry.ComputeOccupiedBoundingBox"/> で計算し、
    /// SkiaSharp の <see cref="SKRectI"/> 形式で返す。配置がない場合は空矩形を返す。
    /// </summary>
    private static SKRectI ComputeOccupiedCellsRect(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items)
    {
        if (items.Count == 0)
            return SKRectI.Empty;

        var placementsForBbox = items
            .Select(i => (i.Placement.Position, i.Placement.OccupySize))
            .ToArray();

        var bbox = PlacementGeometry.ComputeOccupiedBoundingBox(
            grid.CanvasSize, grid.GridCols, grid.GridRows,
            grid.ColWeights, grid.RowWeights,
            placementsForBbox);

        return new SKRectI(bbox.X, bbox.Y, bbox.X + bbox.Width, bbox.Y + bbox.Height);
    }

    /// <summary>
    /// レンダリング結果から α &gt;= <see cref="DrawnPixelAlphaThreshold"/> のピクセルの
    /// バウンディングボックスを計算する。CPU 上の <see cref="SKBitmap"/> にコピーしてから
    /// ピクセルバイトを走査する（SKSurface の GPU テクスチャを直接走査するのは不安定なため）。
    /// <paramref name="clampToRect"/> が指定されればその矩形内のみ走査する。
    /// 全ピクセルが透過 / 閾値未満の場合は空矩形。
    /// </summary>
    private static SKRectI ComputeDrawnPixelsRect(
        SKImage image,
        SKRectI? clampToRect = null,
        SKRectI? renderedGeometryRect = null)
    {
        using var bitmap = SKBitmap.FromImage(image);
        if (bitmap is null) return SKRectI.Empty;

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = bitmap.Bytes; // RGBA8888 (premul) なので RGBA の R,G,B,A 順、A は 4 バイト目
        if (pixels is null || pixels.Length < width * height * 4)
            return SKRectI.Empty;

        var rowStride = bitmap.RowBytes;

        // 走査範囲を占有セル bbox に絞る（外側は確実に透過なので走査不要 + 安全弁）
        int x0 = 0, y0 = 0, x1 = width, y1 = height;
        if (clampToRect is { } clamp && clamp.Width > 0 && clamp.Height > 0)
        {
            x0 = Math.Max(0, clamp.Left);
            y0 = Math.Max(0, clamp.Top);
            x1 = Math.Min(width, clamp.Right);
            y1 = Math.Min(height, clamp.Bottom);
        }
        if (renderedGeometryRect is { } geometry && geometry.Width > 0 && geometry.Height > 0)
        {
            // 半ピクセル配置時、線形補間で画像外周に薄い α 行/列が作られることがある。
            // その行は「画像が配置されている幾何領域」の外側なので、走査範囲から除外する。
            x0 = Math.Max(x0, geometry.Left);
            y0 = Math.Max(y0, geometry.Top);
            x1 = Math.Min(x1, geometry.Right);
            y1 = Math.Min(y1, geometry.Bottom);
        }
        if (x1 <= x0 || y1 <= y0)
            return SKRectI.Empty;

        int minX = x1, minY = y1, maxX = -1, maxY = -1;
        for (var y = y0; y < y1; y++)
        {
            var rowStart = y * rowStride;
            // 行内で最初/最後の "描画あり" ピクセルだけ見れば minX/maxX を更新するのに十分
            int rowMinX = -1, rowMaxX = -1;
            for (var x = x0; x < x1; x++)
            {
                if (pixels[rowStart + x * 4 + 3] >= DrawnPixelAlphaThreshold)
                {
                    if (rowMinX < 0) rowMinX = x;
                    rowMaxX = x;
                }
            }
            if (rowMinX >= 0)
            {
                if (rowMinX < minX) minX = rowMinX;
                if (rowMaxX > maxX) maxX = rowMaxX;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)
            return SKRectI.Empty;

        // SKRectI は Right/Bottom = exclusive 想定で扱う（Subset 引数として）
        return new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>
    /// 各 placement の画像描画先矩形（セル境界クリップ後）の bbox を返す。
    /// DrawnPixels の α 走査範囲と交差させ、サブピクセル補間で生じる外周の薄い透明色を
    /// トリム対象として扱うために使う。<br/>
    /// AutoCrop が有効な placement では、<see cref="DrawOne"/> が cache に保存した
    /// 原画像座標系 bbox を取得して回転後座標系に変換し、ComputeSrcDstRects に渡す
    /// 「実効的な画像サイズ」として使う（DrawOne と同じ dst 矩形を再現する）。
    /// </summary>
    private SKRectI ComputeRenderedGeometryRect(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items)
    {
        SKRectI? union = null;

        foreach (var item in items)
        {
            if (!TryGetSourceAndTransformedImageSize(item, out var sourceW, out var sourceH, out var sw, out var sh))
                continue;

            // Crop が有効なら、回転後座標系の bbox サイズで sw/sh を上書き。
            // DrawOne は src 矩形を Crop オフセット起点で計算するため、ここでも同じ
            // 「Crop 後の論理画像サイズ」で ScalingMode を計算しないと dst 矩形が乖離する。
            // ManualCrop 優先（排他）、それ以外で AutoCrop（cache 経由）。
            (int cx, int cy, int cw, int ch)? cropBbox = null;
            if (item.Copy.ManualCrop is { } manual && !manual.IsFull())
            {
                cropBbox = manual.ToPixelBbox(sourceW, sourceH);
            }
            else if (item.Copy.AutoCrop is { } settings &&
                _autoCropCache.TryGet(item.Copy.AssetId, settings, out var fraction) &&
                !fraction.IsFull())
            {
                cropBbox = fraction.ToPixelBbox(sourceW, sourceH);
            }
            if (cropBbox is { } b && b.cw > 0 && b.ch > 0)
            {
                var rotatedCrop = AutoCropCalculator.TransformRect(
                    new PixelRect(b.cx, b.cy, b.cw, b.ch), sourceW, sourceH, item.Copy.Transform);
                if (rotatedCrop.Width > 0 && rotatedCrop.Height > 0)
                {
                    sw = rotatedCrop.Width;
                    sh = rotatedCrop.Height;
                }
            }

            var cellRect = PlacementGeometry.ComputeDestRect(
                grid.CanvasSize,
                grid.GridCols,
                grid.GridRows,
                grid.ColWeights,
                grid.RowWeights,
                item.Placement.Position,
                item.Placement.OccupySize,
                pixelOffsetX: 0,
                pixelOffsetY: 0);

            var dest = PlacementGeometry.ComputeDestRect(
                grid.CanvasSize,
                grid.GridCols,
                grid.GridRows,
                grid.ColWeights,
                grid.RowWeights,
                item.Placement.Position,
                item.Placement.OccupySize,
                item.Placement.PixelOffsetX,
                item.Placement.PixelOffsetY);

            var (_, dstRect) = ComputeSrcDstRects(sw, sh, dest, item.Copy);
            var visible = Intersect(
                dstRect,
                SKRect.Create(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height));
            if (visible.Width <= 0 || visible.Height <= 0)
                continue;

            var rect = ToPixelSearchRect(visible, grid.CanvasSize.Width, grid.CanvasSize.Height);
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            union = union is null ? rect : Union(union.Value, rect);
        }

        return union ?? SKRectI.Empty;
    }

    /// <summary>
    /// 原画像（回転前）と回転後画像のピクセルサイズを <see cref="SKCodec"/> から取得する。
    /// AutoCrop bbox の回転変換に <paramref name="sourceWidth"/> / <paramref name="sourceHeight"/> が必要なため、
    /// 既存の <c>TryGetTransformedImageSize</c> を拡張している。
    /// </summary>
    private static bool TryGetSourceAndTransformedImageSize(
        PlacementRenderItem item,
        out int sourceWidth, out int sourceHeight,
        out int transformedWidth, out int transformedHeight)
    {
        sourceWidth = 0;
        sourceHeight = 0;
        transformedWidth = 0;
        transformedHeight = 0;
        if (!File.Exists(item.SourceImageAbsolutePath))
            return false;

        using var stream = File.OpenRead(item.SourceImageAbsolutePath);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return false;

        var info = codec.Info;
        sourceWidth = info.Width;
        sourceHeight = info.Height;
        var rotateSwap = item.Copy.Transform.Rotation is Rotation.Cw90 or Rotation.Cw270;
        transformedWidth = rotateSwap ? info.Height : info.Width;
        transformedHeight = rotateSwap ? info.Width : info.Height;
        return transformedWidth > 0 && transformedHeight > 0;
    }

    private static SKRect Intersect(SKRect a, SKRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right <= left || bottom <= top
            ? SKRect.Empty
            : SKRect.Create(left, top, right - left, bottom - top);
    }

    private static SKRectI ToPixelSearchRect(SKRect rect, int canvasWidth, int canvasHeight)
    {
        var left = Math.Clamp((int)Math.Ceiling(rect.Left), 0, canvasWidth);
        var top = Math.Clamp((int)Math.Ceiling(rect.Top), 0, canvasHeight);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right), 0, canvasWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom), 0, canvasHeight);
        return right <= left || bottom <= top
            ? SKRectI.Empty
            : new SKRectI(left, top, right, bottom);
    }

    private static SKRectI Union(SKRectI a, SKRectI b) => new(
        Math.Min(a.Left, b.Left),
        Math.Min(a.Top, b.Top),
        Math.Max(a.Right, b.Right),
        Math.Max(a.Bottom, b.Bottom));

    private Error? DrawOne(
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

        // Crop: ManualCrop 優先、それ以外で AutoCrop（回転前の原画像で外周走査、Cache 経由）。
        // 結果の bbox は原画像座標系。回転後座標系には ApplyTransform 完了後に変換する。
        var autoCropSourceRect = ComputeCropSourceRect(item, decoded);

        using var transformed = ApplyTransform(decoded, item.Copy.Transform);
        using var transformedImage = SKImage.FromBitmap(transformed);
        if (transformedImage is null)
            return Error.Failure(
                "Render.ImageFromBitmapFailed",
                $"画像変換に失敗しました: {item.SourceImageAbsolutePath}");

        // 回転後座標系での AutoCrop 矩形（src 矩形のオフセット元）。
        var autoCropTransformedRect = autoCropSourceRect is { } sourceCrop
            ? AutoCropCalculator.TransformRect(sourceCrop, decoded.Width, decoded.Height, item.Copy.Transform)
            : new PixelRect(0, 0, transformed.Width, transformed.Height);

        // セル領域（PixelOffset 適用前）。クリップ範囲として使う。
        var cellRect = PlacementGeometry.ComputeDestRect(
            grid.CanvasSize,
            grid.GridCols,
            grid.GridRows,
            grid.ColWeights,
            grid.RowWeights,
            item.Placement.Position,
            item.Placement.OccupySize,
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
            item.Placement.OccupySize,
            item.Placement.PixelOffsetX,
            item.Placement.PixelOffsetY);

        if (dest.Width <= 0 || dest.Height <= 0)
            return null;

        // ComputeSrcDstRects には「AutoCrop 後の論理画像サイズ」を渡す。
        // ScalingMode (UniformContain 等) も AutoCrop 後の比率で計算され、見た目通りになる。
        var (srcRect, dstRect) = ComputeSrcDstRects(
            autoCropTransformedRect.Width, autoCropTransformedRect.Height,
            dest, item.Copy);

        // src 矩形を AutoCrop オフセットだけシフトして、実際に切り出す原画像領域を AutoCrop 内に限定する。
        srcRect = SKRect.Create(
            srcRect.Left + autoCropTransformedRect.X,
            srcRect.Top + autoCropTransformedRect.Y,
            srcRect.Width,
            srcRect.Height);

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
    /// ManualCrop 優先 / AutoCrop 次の優先順位で実効的なクロップ bbox を返す（原画像座標系）。
    /// ManualCrop は cache 不要で即時計算、AutoCrop は <see cref="AutoCropCache"/> 経由で
    /// 原画像走査（回転前）の結果を再利用する。クロップ無効なら <c>null</c>。
    /// </summary>
    private PixelRect? ComputeCropSourceRect(PlacementRenderItem item, SKBitmap source)
    {
        // ManualCrop 優先（排他、走査不要）
        if (item.Copy.ManualCrop is { } manual)
        {
            if (manual.IsFull()) return null;
            var (mx, my, mw, mh) = manual.ToPixelBbox(source.Width, source.Height);
            if (mw <= 0 || mh <= 0) return null;
            return new PixelRect(mx, my, mw, mh);
        }

        if (item.Copy.AutoCrop is not { } settings)
            return null;

        var assetId = item.Copy.AssetId;

        // Skia の Decode は Windows で Bgra8888 を返すため、Bytes を RGBA として走査する前に
        // Rgba8888+Unpremul に正規化する（R/B swap で色一致が壊れるのを防ぐ）。
        // Cache miss 時のみ走査するので毎回コピーは発生しない。
        var fraction = _autoCropCache.GetOrCompute(assetId, settings, () =>
        {
            if (source.Width <= 0 || source.Height <= 0)
                return AutoCropFraction.Full;
            var normalized = SkiaPixelHelper.EnsureRgbaUnpremul(source, out var ownsNormalized);
            try
            {
                var pixels = normalized.Bytes;
                if (pixels is null || pixels.Length == 0)
                    return AutoCropFraction.Full;
                var bbox = AutoCropCalculator.Compute(pixels, normalized.Width, normalized.Height, normalized.RowBytes, settings);
                if (bbox.Width <= 0 || bbox.Height <= 0)
                    return AutoCropFraction.Full;
                return new AutoCropFraction(
                    (double)bbox.X / normalized.Width,
                    (double)bbox.Y / normalized.Height,
                    (double)bbox.Width / normalized.Width,
                    (double)bbox.Height / normalized.Height);
            }
            finally
            {
                if (ownsNormalized) normalized.Dispose();
            }
        });

        if (fraction.IsFull())
            return null;

        var (x, y, w, h) = fraction.ToPixelBbox(source.Width, source.Height);
        if (w <= 0 || h <= 0)
            return null;
        return new PixelRect(x, y, w, h);
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

        // 位置決め（画像 ≤ セル）とトリミング（画像 > セル）を単一の Alignment アンカーで表現。
        // CSS background-position 等の業界標準に倣う。旧版は TrimmingAnchor / Alignment の
        // 2 アンカー設計だったが、概念的に同じものを 2 つ持つだけだったため統合した。
        var anchorX = ToAnchor1D(copy.Alignment.X);
        var anchorY = ToAnchor1D(copy.Alignment.Y);

        var (srcX, srcW, dstX, dstW) = ComputeAxis(sw, dest.X, dest.Width, scale, anchorX);
        var (srcY, srcH, dstY, dstH) = ComputeAxis(sh, dest.Y, dest.Height, scale, anchorY);

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
        Anchor1D anchor)
    {
        // 画像全体（src 全体）を scale 倍した dst に常に配置する。
        // 画像 ≤ セル のときは pad >= 0 で anchor の位置に配置（中央/左右）。
        // 画像 > セル のときは pad < 0 で dst が cell 範囲を超え、上位の <see cref="SKCanvas.ClipRect"/>
        // でセル境界外がカットされる。PixelOffset で dst を動かしても表示サイズが
        // 縮まず、見せる src 部分が変わるだけ（View の <c>Image + Translate + ClipToBounds</c> と整合）。
        var drawSize = srcSize * scale;
        var pad = dstSize - drawSize;
        var dstOffset = anchor switch
        {
            Anchor1D.Start => 0.0,
            Anchor1D.End => pad,
            _ => pad / 2.0,
        };
        var drawStart = dstStart + dstOffset;
        if (IsNearlyInteger(drawSize))
        {
            // 画像の描画サイズが整数ピクセルなのに開始位置だけが .5 になると、
            // 線形補間で外周に 1px 程度の半透明行/列が発生する。
            drawStart = Math.Round(drawStart, MidpointRounding.AwayFromZero);
            drawSize = Math.Round(drawSize, MidpointRounding.AwayFromZero);
        }
        return (0.0, srcSize, drawStart, drawSize);
    }

    private static bool IsNearlyInteger(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.000001;
}
