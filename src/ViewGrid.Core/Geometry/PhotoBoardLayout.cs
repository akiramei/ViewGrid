using System;
using System.Collections.Generic;
using System.Linq;
using ViewGrid.Core.Entities;
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
    /// chaos の二段階カーブの境界。<c>[0, FrameRampThreshold]</c> でフレーム / シャドウが
    /// 0→100% にランプし、<c>[FrameRampThreshold, 1.0]</c> で散らかし (回転 / ジッター /
    /// ピボット) が 0→100% にランプする。これで chaos の中間値にも「写真ボードらしさ」が
    /// 段階的に出現し、線形スケールだと「全部薄い」状態になるのを避ける。
    /// </summary>
    private const double FrameRampThreshold = 0.20;

    /// <summary>
    /// chaos=1 時の最大グリッド拡張倍率。各 placement の中心位置をキャンバス中心から
    /// 外側に <c>1.0 + MaxExpansionFactor * disorderRamp</c> 倍に広げることで、
    /// 画像がセルを覆い尽くしている密集グリッドでも散らかし感を出す。
    /// disorderRamp と連動するので chaos &lt;= 0.20 では拡張なし (整列状態を維持)。
    /// </summary>
    private const double MaxExpansionFactor = 0.40;

    /// <summary>
    /// 全体ドリフトの最大量 (セル短辺に対する比率)。レンダリング毎に 1 つの方向を
    /// PRNG で決め、全 placement に同方向の微小シフトを加える。「机の上に置いた
    /// ときの手の癖で全体的に右下に寄る」みたいな統一感を生み、各 placement が
    /// バラバラに散る人工感を抑える。
    /// </summary>
    private const double MaxGlobalDriftFraction = 0.06;

    /// <summary>
    /// 重なり (overlap) を解禁する chaos 閾値。これ未満では各 placement の拡張倍率は
    /// 一律 (= グリッド全体が同じ比率で広がる)。これ以上では per-item で拡張倍率を
    /// ばらつかせ、一部の placement が内側に寄って隣の placement と重なる「写真を
    /// 軽く重ねて並べた」感を出す。
    /// </summary>
    private const double OverlapChaosThreshold = 0.6;

    /// <summary>
    /// 各 placement の PhotoBoard 描画パラメータを計算する。
    /// </summary>
    /// <param name="baseRects">配置順 (PlacementOrder 昇順) で渡す入力。</param>
    /// <param name="canvas">グリッドの論理キャンバスサイズ。グリッド拡張 (散らかし時の
    /// 中心からの放射状シフト) の基準点として使う。</param>
    /// <param name="chaos">[0, 1] の連続値。0 でフレーム / シャドウ / ジッター / 回転 / 拡張すべて 0。</param>
    /// <param name="seed">決定論的 PRNG シード。</param>
    public static IReadOnlyList<PhotoBoardItem> Compute(
        IReadOnlyList<PlacementBaseRect> baseRects,
        PixelSize canvas,
        double chaos,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(baseRects);
        if (baseRects.Count == 0)
            return Array.Empty<PhotoBoardItem>();

        var clamped = Math.Clamp(chaos, 0.0, 1.0);
        // 二段階カーブ: 写真キャラクター (frame + shadow) は早めに飽和、散らかし
        // (jitter + rotation + pivot + 拡張) はその後にランプ。chaos の中間値で「写真風」が
        // 明確に認識できる状態を作る。
        var frameRamp = Math.Min(1.0, clamped / FrameRampThreshold);
        var disorderRamp = clamped <= FrameRampThreshold
            ? 0.0
            : (clamped - FrameRampThreshold) / (1.0 - FrameRampThreshold);

        // グリッド拡張倍率: chaos=0 → 1.0 (キャンバスそのまま)、chaos=1 → 1.0 + MaxExpansionFactor。
        // 各 placement の中心がキャンバス中心から放射状に広がる (画像がセルを覆っていても
        // 隙間ができ「散らかし感」を生む)。
        var expansionFactor = 1.0 + MaxExpansionFactor * disorderRamp;
        var canvasCenterX = canvas.Width / 2.0;
        var canvasCenterY = canvas.Height / 2.0;

        var rng = new Random(seed);

        // 全体ドリフト: レンダリング毎に 1 つの方向 (角度) と量を決める。
        // 全 placement に同じシフトが加わるので「手の癖」感が出る。
        var globalDriftAngle = rng.NextDouble() * 2.0 * Math.PI;
        var globalDriftMag = rng.NextDouble() * MaxGlobalDriftFraction; // [0, max]
        var globalDriftXFactor = Math.Cos(globalDriftAngle) * globalDriftMag;
        var globalDriftYFactor = Math.Sin(globalDriftAngle) * globalDriftMag;

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
            // 回転は triangular distribution (NextDouble() - NextDouble()) で 0° 付近を厚く
            // 取る。一様分布だと「全部斜め」に見える均質な人工感が出るのを抑え、
            // 0° 付近 = 真っ直ぐ置かれた写真が混じる「自然な配置」感を作る。範囲は同じ [-1, 1]。
            var rotationFactor = rng.NextDouble() - rng.NextDouble();
            var shadowOffsetXJitter = (rng.NextDouble() * 2.0 - 1.0);
            var shadowOffsetYJitter = (rng.NextDouble() * 2.0 - 1.0);
            var shadowSigmaJitter = (rng.NextDouble() * 2.0 - 1.0);

            // グリッド拡張オフセット: セル中心がキャンバス中心から expansionFactor 倍に広がる。
            // chaos=0 で 0、chaos=1 で約 30% 外側へ移動 (4 隅は最も大きく動く)。
            // chaos > OverlapChaosThreshold (=0.6) では per-item で拡張倍率を [-0.4, +1.0] の
            // 範囲でばらつかせ、一部 placement が内側に寄って重なりを作る。
            var cellCenterX = rect.X + rect.Width / 2.0;
            var cellCenterY = rect.Y + rect.Height / 2.0;
            var perItemExpansionMul = clamped > OverlapChaosThreshold
                ? (rng.NextDouble() * 1.4 - 0.4)  // [-0.4, +1.0]
                : 1.0;
            var perItemExpansionFactor = 1.0 + (expansionFactor - 1.0) * perItemExpansionMul;
            var expansionOffsetX = (cellCenterX - canvasCenterX) * (perItemExpansionFactor - 1.0);
            var expansionOffsetY = (cellCenterY - canvasCenterY) * (perItemExpansionFactor - 1.0);

            // 散らかし (jitter / rotation / pivot + 拡張 + 全体ドリフト) は disorderRamp でスケール。
            // chaos <= FrameRampThreshold (既定 0.20) では 0 → 写真は整列して並ぶ。
            // 全体ドリフトは全 placement に同方向の微小シフトを加える「手の癖」効果。
            var offsetX = expansionOffsetX + disorderRamp * minSide * (
                MaxJitterFraction * itemJitterX
                + MaxRowColBiasFraction * rowBiasX
                + MaxRowColBiasFraction * colBiasX
                + globalDriftXFactor);
            var offsetY = expansionOffsetY + disorderRamp * minSide * (
                MaxJitterFraction * itemJitterY
                + MaxRowColBiasFraction * rowBiasY
                + MaxRowColBiasFraction * colBiasY
                + globalDriftYFactor);

            var rotationDeg = disorderRamp * MaxRotationDeg * rotationFactor;
            var rotationPivotOffsetX = disorderRamp * minSide * MaxRotationPivotFraction * pivotJitterX;
            var rotationPivotOffsetY = disorderRamp * minSide * MaxRotationPivotFraction * pivotJitterY;

            // 写真キャラクター (frame + shadow) は frameRamp でスケール。
            // chaos が 0 → FrameRampThreshold で 0 → 100% にランプ → 早めに飽和して
            // 「写真ボード風」のシルエットを認識可能にする。
            var frameSidePx = (int)Math.Round(MaxFrameSidePx * frameRamp);
            var frameBottomPx = (int)Math.Round(MaxFrameBottomPx * frameRamp);
            var frameAlpha = (byte)Math.Round(255.0 * frameRamp);

            var shadowOffsetX = frameRamp * (BaseShadowOffsetX + ShadowOffsetJitterPx * shadowOffsetXJitter);
            var shadowOffsetY = frameRamp * (BaseShadowOffsetY + ShadowOffsetJitterPx * shadowOffsetYJitter);
            var shadowSigma = frameRamp * Math.Max(0.0, BaseShadowSigma + ShadowSigmaJitterPx * shadowSigmaJitter);
            var shadowAlpha = (byte)Math.Round(ShadowMaxAlpha * frameRamp);

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
        PixelSize canvas,
        double chaos)
    {
        ArgumentNullException.ThrowIfNull(baseRects);
        if (baseRects.Count == 0)
            return 0;

        var clamped = Math.Clamp(chaos, 0.0, 1.0);
        if (clamped <= 0.0)
            return 0;

        // Compute と同じ二段階カーブを使う。frame/shadow は frameRamp、
        // 位置オフセット / 回転 / 拡張は disorderRamp に従う。
        var frameRamp = Math.Min(1.0, clamped / FrameRampThreshold);
        var disorderRamp = clamped <= FrameRampThreshold
            ? 0.0
            : (clamped - FrameRampThreshold) / (1.0 - FrameRampThreshold);

        var maxMinSide = baseRects.Max(r => Math.Min(r.Rect.Width, r.Rect.Height));
        var maxLongSide = baseRects.Max(r => Math.Max(r.Rect.Width, r.Rect.Height));

        // 位置 (jitter + row/col bias + 全体ドリフトの最悪ケース合算)
        var maxOffset = disorderRamp * maxMinSide
            * (MaxJitterFraction + 2.0 * MaxRowColBiasFraction + MaxGlobalDriftFraction);

        // グリッド拡張: 4 隅の placement は最大 (canvas/2) * (expansionFactor - 1) 外側へ動く
        var expansionGrowth = Math.Max(canvas.Width, canvas.Height) / 2.0
            * MaxExpansionFactor * disorderRamp;

        // 回転による対角伸長分: 回転後 bbox の最大増分は (W+H)/2 * sin(θ) 程度
        var maxRotRad = disorderRamp * MaxRotationDeg * Math.PI / 180.0;
        var rotationGrowth = (maxLongSide / 2.0) * Math.Abs(Math.Sin(maxRotRad))
                           + (maxMinSide / 2.0) * Math.Abs(Math.Sin(maxRotRad));

        // フレーム + シャドウ
        var frame = MaxFrameBottomPx * frameRamp;
        var shadow = (BaseShadowOffsetY + ShadowOffsetJitterPx
                    + 3.0 * (BaseShadowSigma + ShadowSigmaJitterPx)) * frameRamp;

        return (int)Math.Ceiling(maxOffset + expansionGrowth + rotationGrowth + frame + shadow + 4.0);
    }
}
