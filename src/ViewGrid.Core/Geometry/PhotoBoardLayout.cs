using System;
using System.Collections.Generic;
using System.Linq;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Geometry;

/// <summary>
/// PhotoBoard モードの描画変換 1 件分。元のセル矩形 (<see cref="BaseRect"/>) に対して
/// 「整列 ↔ 散らかし」軸 (chaos) に応じたジッター位置 / 回転 / フレーム / シャドウ情報を
/// 保持する。renderer はこのレコードを読むだけで「どこに何を描けばよいか」を判断できる。
/// すべてのフィールドは chaos に線形比例し、chaos=0 で全て 0 / 透明 → renderer 側の分岐で
/// <see cref="Entities.TrimMode.None"/> と同一出力になる。
/// </summary>
public sealed record PhotoBoardItem(
    PixelRect BaseRect,
    double OffsetX,
    double OffsetY,
    double RotationDeg,
    double RotationPivotOffsetX,
    double RotationPivotOffsetY,
    int FrameSidePx,
    int FrameBottomPx,
    byte FrameAlpha,
    double ShadowOffsetX,
    double ShadowOffsetY,
    double ShadowSigma,
    byte ShadowAlpha);

/// <summary>
/// PhotoBoardLayout への入力 1 件分。<see cref="RowIndex"/> / <see cref="ColIndex"/> は
/// 「同じ行 / 列に属する placement 同士で同方向の揺らぎを共有する」バイアス計算に必須。
/// </summary>
public sealed record PlacementBaseRect(int RowIndex, int ColIndex, PixelRect Rect);

/// <summary>
/// PhotoBoard モードの位置 / 回転 / フレーム / シャドウを純粋関数で計算する。
/// 決定論的 PRNG (<see cref="Random"/> with fixed seed = .NET 6+ Xoshiro256**) を使うため、
/// 同じ入力 + 同じシードで常に同じ結果。
///
/// <para>
/// 「人間が手で写真を並べた」感を出すため、単純な i.i.d. ジッターに加えて以下を含める:
/// <list type="bullet">
/// <item>列 / 行ごとのバイアス (同じ行は似た方向に揺らぐ波状の不規則性)</item>
/// <item>回転中心ジッター (回転ピボットを baseCenter からずらして「手で置いた」非対称感)</item>
/// <item>per-item シャドウジッター (offset / blur / alpha を ±10% 程度ばらす)</item>
/// </list>
/// </para>
/// </summary>
public static class PhotoBoardLayout
{
    // ─── 内部定数 (chaos=1 時の最大値) ───
    private const int MaxFrameSidePx = 12;       // ポラロイド風: 上下左
    private const int MaxFrameBottomPx = 36;     // ポラロイド風: 下のみ太い
    private const double BaseShadowOffsetX = 2.0;
    private const double BaseShadowOffsetY = 4.0;
    private const double BaseShadowSigma = 4.0;
    private const double ShadowOffsetJitterPx = 1.0;  // ±1px
    private const double ShadowSigmaJitterPx = 0.5;   // ±0.5
    private const byte ShadowMaxAlpha = 64;
    // バイアス優位 (per-item jitter < row/col bias) にすることで、同じ行 / 列の placement が
    // 同方向に揺らぐ「波状の不規則性」を視覚的に強調する。i.i.d. ジッターが優位になると
    // 「散らかしてるけど整って見える」中途半端な印象になりやすい。
    private const double MaxJitterFraction = 0.05;       // セル短辺に対する比率
    private const double MaxRowColBiasFraction = 0.10;
    private const double MaxRotationDeg = 8.0;
    private const double MaxRotationPivotFraction = 0.10;

    /// <summary>
    /// 各 placement の PhotoBoard 描画パラメータを計算する。
    /// </summary>
    /// <param name="baseRects">配置順 (PlacementOrder 昇順) で渡す入力。</param>
    /// <param name="chaos">[0, 1] の連続値。0 でフレーム / シャドウ / ジッター / 回転すべて 0。</param>
    /// <param name="seed">決定論的 PRNG シード。</param>
    public static IReadOnlyList<PhotoBoardItem> Compute(
        IReadOnlyList<PlacementBaseRect> baseRects,
        double chaos,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(baseRects);
        if (baseRects.Count == 0)
            return Array.Empty<PhotoBoardItem>();

        var clamped = Math.Clamp(chaos, 0.0, 1.0);
        var rng = new Random(seed);

        // 列・行バイアスの事前計算: 同じ行 / 列の placement は同じ方向に揺らぐ
        var rowBiases = new Dictionary<int, (double X, double Y)>();
        var colBiases = new Dictionary<int, (double X, double Y)>();

        // 算出順を決定的に保つため、登場順 (baseRects の順) で初出時に一度だけ生成
        foreach (var baseRect in baseRects)
        {
            if (!rowBiases.ContainsKey(baseRect.RowIndex))
            {
                var bx = (rng.NextDouble() * 2.0 - 1.0);
                var by = (rng.NextDouble() * 2.0 - 1.0);
                rowBiases[baseRect.RowIndex] = (bx, by);
            }
            if (!colBiases.ContainsKey(baseRect.ColIndex))
            {
                var bx = (rng.NextDouble() * 2.0 - 1.0);
                var by = (rng.NextDouble() * 2.0 - 1.0);
                colBiases[baseRect.ColIndex] = (bx, by);
            }
        }

        var items = new List<PhotoBoardItem>(baseRects.Count);
        foreach (var baseRect in baseRects)
        {
            var rect = baseRect.Rect;
            var minSide = Math.Min(rect.Width, rect.Height);
            var (rowBiasX, rowBiasY) = rowBiases[baseRect.RowIndex];
            var (colBiasX, colBiasY) = colBiases[baseRect.ColIndex];

            // per-item ジッター (位置 / 回転ピボット / 回転角 / シャドウ)
            var itemJitterX = (rng.NextDouble() * 2.0 - 1.0);
            var itemJitterY = (rng.NextDouble() * 2.0 - 1.0);
            var pivotJitterX = (rng.NextDouble() * 2.0 - 1.0);
            var pivotJitterY = (rng.NextDouble() * 2.0 - 1.0);
            var rotationFactor = (rng.NextDouble() * 2.0 - 1.0);
            var shadowOffsetXJitter = (rng.NextDouble() * 2.0 - 1.0);
            var shadowOffsetYJitter = (rng.NextDouble() * 2.0 - 1.0);
            var shadowSigmaJitter = (rng.NextDouble() * 2.0 - 1.0);

            // 線形に chaos でスケール (chaos=0 → 全項目 0)
            var offsetX = clamped * minSide * (
                MaxJitterFraction * itemJitterX
                + MaxRowColBiasFraction * rowBiasX
                + MaxRowColBiasFraction * colBiasX);
            var offsetY = clamped * minSide * (
                MaxJitterFraction * itemJitterY
                + MaxRowColBiasFraction * rowBiasY
                + MaxRowColBiasFraction * colBiasY);

            var rotationDeg = clamped * MaxRotationDeg * rotationFactor;
            var rotationPivotOffsetX = clamped * minSide * MaxRotationPivotFraction * pivotJitterX;
            var rotationPivotOffsetY = clamped * minSide * MaxRotationPivotFraction * pivotJitterY;

            var frameSidePx = (int)Math.Round(MaxFrameSidePx * clamped);
            var frameBottomPx = (int)Math.Round(MaxFrameBottomPx * clamped);
            var frameAlpha = (byte)Math.Round(255.0 * clamped);

            var shadowOffsetX = clamped * (BaseShadowOffsetX + ShadowOffsetJitterPx * shadowOffsetXJitter);
            var shadowOffsetY = clamped * (BaseShadowOffsetY + ShadowOffsetJitterPx * shadowOffsetYJitter);
            var shadowSigma = clamped * Math.Max(0.0, BaseShadowSigma + ShadowSigmaJitterPx * shadowSigmaJitter);
            var shadowAlpha = (byte)Math.Round(ShadowMaxAlpha * clamped);

            items.Add(new PhotoBoardItem(
                rect,
                offsetX, offsetY,
                rotationDeg,
                rotationPivotOffsetX, rotationPivotOffsetY,
                frameSidePx, frameBottomPx, frameAlpha,
                shadowOffsetX, shadowOffsetY, shadowSigma, shadowAlpha));
        }

        return items;
    }

    /// <summary>
    /// PhotoBoard モードの最終キャンバスに必要なマージン (4 辺それぞれに加算) を計算する。
    /// 位置オフセット + 回転対角線分の伸び + フレーム + シャドウぼかしすべての最大値を含む。
    /// </summary>
    public static int RequiredCanvasMargin(
        IReadOnlyList<PlacementBaseRect> baseRects,
        double chaos)
    {
        ArgumentNullException.ThrowIfNull(baseRects);
        if (baseRects.Count == 0)
            return 0;

        var clamped = Math.Clamp(chaos, 0.0, 1.0);
        if (clamped <= 0.0)
            return 0;

        var maxMinSide = baseRects.Max(r => Math.Min(r.Rect.Width, r.Rect.Height));
        var maxLongSide = baseRects.Max(r => Math.Max(r.Rect.Width, r.Rect.Height));

        // 位置 (jitter + row/col bias の最悪ケース合算)
        var maxOffset = clamped * maxMinSide
            * (MaxJitterFraction + 2.0 * MaxRowColBiasFraction);

        // 回転による対角伸長分: 回転後 bbox の最大増分は (W+H)/2 * sin(θ) 程度
        var maxRotRad = clamped * MaxRotationDeg * Math.PI / 180.0;
        var rotationGrowth = (maxLongSide / 2.0) * Math.Abs(Math.Sin(maxRotRad))
                           + (maxMinSide / 2.0) * Math.Abs(Math.Sin(maxRotRad));

        // フレーム + シャドウ
        var frame = MaxFrameBottomPx * clamped;
        var shadow = (BaseShadowOffsetY + ShadowOffsetJitterPx
                    + 3.0 * (BaseShadowSigma + ShadowSigmaJitterPx)) * clamped;

        return (int)Math.Ceiling(maxOffset + rotationGrowth + frame + shadow + 4.0);
    }
}
