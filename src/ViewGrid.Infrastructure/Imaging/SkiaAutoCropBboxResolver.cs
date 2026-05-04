using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Imaging;

/// <summary>
/// <see cref="IAutoCropBboxResolver"/> の SkiaSharp 実装。<see cref="AutoCropCache"/> と
/// 同じ singleton インスタンスを共有することで、レンダラ・View・Use case の 3 経路で
/// 同一比率を再利用する（同じキーで重複走査しない）。
/// <para>
/// Cache miss 時は <see cref="SKBitmap.Decode(string)"/> で**原画像**を読み込み、
/// <see cref="AutoCropCalculator.Compute"/> で外周走査して bbox を計算 →
/// <see cref="AutoCropFraction"/> に変換して保存する。スレッド安全。
/// </para>
/// </summary>
internal sealed class SkiaAutoCropBboxResolver : IAutoCropBboxResolver
{
    private readonly AutoCropCache _cache;

    public SkiaAutoCropBboxResolver(AutoCropCache cache)
    {
        _cache = cache;
    }

    public Task<AutoCropFraction?> ResolveAsync(
        Guid assetId,
        string sourceImageAbsolutePath,
        AutoCropSettings settings,
        CancellationToken ct = default)
    {
        // CPU バウンドな decode + 走査なので Task.Run で off-load する。
        return Task.Run<AutoCropFraction?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Cache hit: 比率がすでに保存されている。クロップ無効（Full）も上位で no-op 化される。
            if (_cache.TryGet(assetId, settings, out var cached))
            {
                return cached.IsFull() ? null : cached;
            }

            if (!File.Exists(sourceImageAbsolutePath))
                return null;

            using var raw = SKBitmap.Decode(sourceImageAbsolutePath);
            if (raw is null)
                return null;

            // Skia の Decode は Windows で Bgra8888 を返すため、Bytes を RGBA として
            // 走査する前に Rgba8888+Unpremul に正規化する（R/B swap で色一致が壊れるのを防ぐ）。
            var bitmap = SkiaPixelHelper.EnsureRgbaUnpremul(raw, out var ownsBitmap);
            try
            {
                var pixels = bitmap.Bytes;
                if (pixels is null || pixels.Length == 0)
                    return null;

                // bitmap は finally で Dispose されるため、クロージャに渡す値はすべてローカル変数に取り出す
                // (Qodana: AccessToDisposedClosure 回避)。GetOrCompute は同期実行なので実害はないが静的契約を整える。
                var width = bitmap.Width;
                var height = bitmap.Height;
                var rowBytes = bitmap.RowBytes;

                var fraction = _cache.GetOrCompute(assetId, settings, () =>
                {
                    var bbox = AutoCropCalculator.Compute(pixels, width, height, rowBytes, settings);
                    if (bbox.Width <= 0 || bbox.Height <= 0 || width <= 0 || height <= 0)
                        return AutoCropFraction.Full;
                    return new AutoCropFraction(
                        (double)bbox.X / width,
                        (double)bbox.Y / height,
                        (double)bbox.Width / width,
                        (double)bbox.Height / height);
                });

                return fraction.IsFull() ? null : fraction;
            }
            finally
            {
                if (ownsBitmap) bitmap.Dispose();
            }
        }, ct);
    }
}
