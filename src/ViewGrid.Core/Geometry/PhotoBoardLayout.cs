using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Geometry;

/// <summary>
/// PhotoBoard モードの描画変換 1 件分。元のセル矩形 (<see cref="BaseRect"/>) に対して
/// <see cref="PhotoBoardStyleCoefficients"/> から導出されたジッター位置 / 回転 / フレーム /
/// シャドウ情報を保持する。renderer はこのレコードを読むだけで「どこに何を描けばよいか」
/// を判断できる。係数すべてが 0 (= <see cref="PhotoBoardStyleCoefficients.Off"/>) のとき
/// 全フィールド 0 / 透明となり、 renderer 側で <see cref="Entities.OutputMode.Normal"/> +
/// <see cref="Entities.TrimMode.None"/> と同一出力に分岐できる。
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
    // ─── 内部定数 (各係数 = 1.0 時の最大値) ───
    private const int MaxFrameSidePx = 12;       // ポラロイド風: 上下左
    private const int MaxFrameBottomPx = 36;     // ポラロイド風: 下のみ太い
    private const double BaseShadowOffsetX = 2.0;
    private const double BaseShadowOffsetY = 4.0;
    private const double BaseShadowSigma = 4.0;
    private const double ShadowOffsetJitterPx = 1.0;  // ±1px
    private const double ShadowSigmaJitterPx = 1.0;   // ±1.0 (UI 感を消すために強化)
    private const byte ShadowMaxAlpha = 64;
    // バイアス優位 (per-item jitter < row/col bias) にすることで、同じ行 / 列の placement が
    // 同方向に揺らぐ「波状の不規則性」を視覚的に強調する。i.i.d. ジッターが優位になると
    // 「散らかしてるけど整って見える」中途半端な印象になりやすい。
    private const double MaxJitterFraction = 0.05;       // セル短辺に対する比率
    private const double MaxRowColBiasFraction = 0.10;
    private const double MaxRotationDeg = 8.0;
    // 回転中心オフセット: 0.10 だと小さい回転角での視覚効果が薄い (8° で約 3px の動き)。
    // 0.18 まで強化して「手で置いた」非対称感をはっきり出す。
    private const double MaxRotationPivotFraction = 0.18;

    /// <summary>
    /// 全体ドリフトの最大量 (セル短辺に対する比率)。レンダリング毎に 1 つの方向を
    /// PRNG で決め、全 placement に同方向の微小シフトを加える。「机の上に置いた
    /// ときの手の癖で全体的に右下に寄る」みたいな統一感を生み、各 placement が
    /// バラバラに散る人工感を抑える。
    /// </summary>
    private const double MaxGlobalDriftFraction = 0.06;

    /// <summary>セル短辺に対する重なり nudge の最大シフト量。0.30 だと
    /// 「崩しすぎ」と感じるため 0.20 に抑制。任意角のいずれかに nudge を加える。</summary>
    private const double OverlapNudgeFraction = 0.20;

    /// <summary>
    /// 重なり nudge で「外向き bias」を効かせる確率。これ以下のときは純粋な 360° ランダム、
    /// これ以上のときはキャンバス中心から外向き方向を中心に ±<see cref="OutwardSpreadHalf"/>
    /// の角度範囲でランダム化する。中央セルが内向きにのみ寄って起きる「中央集中」を抑える。
    /// </summary>
    private const double OutwardBiasProbability = 0.6;

    /// <summary>外向き bias 時のスプレッド半幅 (ラジアン)。π/2 = ±90 度。</summary>
    private const double OutwardSpreadHalf = Math.PI / 2.0;

    /// <summary>ペア分解の発火距離閾値 (平均セル短辺の倍数)。これ以内 + 同方向 = ペア判定。</summary>
    private const double PairSeparationDistanceFactor = 0.8;

    /// <summary>ペア分解の発火角度閾値 (度)。回転がこれ以下の差なら「同方向」とみなす。</summary>
    private const double PairSeparationRotationThresholdDeg = 2.0;

    /// <summary>ペア分解 nudge 量 (平均セル短辺の倍数)。10px 程度。</summary>
    private const double PairSeparationNudgeFraction = 0.05;

    /// <summary>
    /// 各 placement の PhotoBoard 描画パラメータを計算する。
    /// </summary>
    /// <param name="baseRects">配置順 (PlacementOrder 昇順) で渡す入力。</param>
    /// <param name="canvas">グリッドの論理キャンバスサイズ。グリッド拡張 (散らかし時の
    /// 中心からの放射状シフト) の基準点として使う。</param>
    /// <param name="coefs">レンダリング駆動 9 係数。スタイル + 強度から導出される。</param>
    /// <param name="seed">決定論的 PRNG シード。</param>
    public static IReadOnlyList<PhotoBoardItem> Compute(
        IReadOnlyList<PlacementBaseRect> baseRects,
        PixelSize canvas,
        PhotoBoardStyleCoefficients coefs,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(baseRects);
        ArgumentNullException.ThrowIfNull(coefs);
        if (baseRects.Count == 0)
            return Array.Empty<PhotoBoardItem>();

        // 各係数を読みやすい局所名に展開。係数はファクトリ側で [0, 1] / [1.0, +∞) に
        // クランプ済みなので、ここでの再クランプは不要。
        var frameStrength = coefs.FrameStrength;
        var shadowStrength = coefs.ShadowStrength;
        var rotationStrength = coefs.RotationStrength;
        var jitterStrength = coefs.JitterStrength;
        var driftStrength = coefs.DriftStrength;
        var anchorDecay = coefs.AnchorDecay;
        var expansionFactor = coefs.Expansion;
        var canvasCenterX = canvas.Width / 2.0;
        var canvasCenterY = canvas.Height / 2.0;

        var rng = new Random(seed);

        // アンカー (主役) 選び: 1 件だけ「起点」として offset / rotation を減衰させる。
        // 群れ配置感を消し「意図を持ったランダム」に見せる仕掛け。
        var anchorIndex = baseRects.Count > 0 ? rng.Next(baseRects.Count) : -1;

        // 全体ドリフト: レンダリング毎に 1 つの方向 (角度) と量を決める。
        // 全 placement に同じシフトが加わるので「手の癖」感が出る。
        // 量は driftStrength でスケール (0 でドリフト完全停止)。
        var globalDriftAngle = rng.NextDouble() * 2.0 * Math.PI;
        var globalDriftMag = rng.NextDouble() * MaxGlobalDriftFraction * driftStrength; // [0, max]
        var globalDriftXFactor = Math.Cos(globalDriftAngle) * globalDriftMag;
        var globalDriftYFactor = Math.Sin(globalDriftAngle) * globalDriftMag;

        // 明示的重なり (overlap nudge): per-item 独立で OverlapProbability の確率で発火。
        // 0 で完全停止、1 で全件 nudge。Z 順は PlacementOrder のままなので、後ろ placement
        // が前 placement の下に潜り込む見え方になる。
        var overlapNudges = new Dictionary<int, (double X, double Y)>();
        var overlapProbability = baseRects.Count > 1 ? coefs.OverlapProbability : 0.0;
        if (overlapProbability > 0.0)
        {
            for (int i = 0; i < baseRects.Count; i++)
            {
                if (rng.NextDouble() >= overlapProbability)
                    continue;

                var rect = baseRects[i].Rect;
                var nudgeMag = OverlapNudgeFraction * Math.Min(rect.Width, rect.Height);

                // 方向決定: 360° 連続でランダム化。8 方向だけだと「縦か横にズレた」
                // 機械感が残るため任意角を許可。
                // さらに OutwardBiasProbability の確率で「外向き」ベース角に bias して
                // 中央セルが内向きにのみ寄って起きる「中央集中」を抑える。
                var cellCenterXLocal = rect.X + rect.Width / 2.0;
                var cellCenterYLocal = rect.Y + rect.Height / 2.0;
                var dirX = cellCenterXLocal - canvasCenterX;
                var dirY = cellCenterYLocal - canvasCenterY;
                var dirMag = Math.Sqrt(dirX * dirX + dirY * dirY);

                double angle;
                if (dirMag > 1e-6 && rng.NextDouble() < OutwardBiasProbability)
                {
                    var outwardAngle = Math.Atan2(dirY, dirX);
                    angle = outwardAngle + (rng.NextDouble() - 0.5) * 2.0 * OutwardSpreadHalf;
                }
                else
                {
                    angle = rng.NextDouble() * 2.0 * Math.PI;
                }

                overlapNudges[i] = (Math.Cos(angle) * nudgeMag, Math.Sin(angle) * nudgeMag);
            }
        }

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
        for (int itemIdx = 0; itemIdx < baseRects.Count; itemIdx++)
        {
            var baseRect = baseRects[itemIdx];
            var rect = baseRect.Rect;
            var minSide = Math.Min(rect.Width, rect.Height);
            var (overlapNudgeX, overlapNudgeY) = overlapNudges.TryGetValue(itemIdx, out var nudge)
                ? nudge
                : (0.0, 0.0);
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
            // シャドウ alpha も per-item で ±20% 揺らがせる (UI 感を消す)
            var shadowAlphaJitter = (rng.NextDouble() * 2.0 - 1.0);

            // グリッド拡張オフセット: セル中心がキャンバス中心から expansionFactor 倍に広がる。
            // expansion=1.0 で 0、 expansion=1.40 で約 40% 外側へ移動 (4 隅は最も大きく動く)。
            // OverlapProbability > 0 のスタイル (= 重なりを作る系) では per-item で拡張倍率を
            // [-0.4, +1.0] の範囲でばらつかせ、一部 placement が内側に寄って重なりを作る。
            var cellCenterX = rect.X + rect.Width / 2.0;
            var cellCenterY = rect.Y + rect.Height / 2.0;
            var perItemExpansionMul = coefs.OverlapProbability > 0.0
                ? (rng.NextDouble() * 1.4 - 0.4)  // [-0.4, +1.0]
                : 1.0;
            var perItemExpansionFactor = 1.0 + (expansionFactor - 1.0) * perItemExpansionMul;
            var expansionOffsetX = (cellCenterX - canvasCenterX) * (perItemExpansionFactor - 1.0);
            var expansionOffsetY = (cellCenterY - canvasCenterY) * (perItemExpansionFactor - 1.0);

            // 散らかし (jitter + 行/列 bias) は jitterStrength でスケール、
            // 全体ドリフトは driftStrength で別途スケール (globalDrift*Factor 構築時に乗算済み)。
            // overlapNudge は明示的重なりのシフト (選ばれた K 件のみ非 0)。
            // expansionOffset は coefs.Expansion で決まり intensity の影響をすでに織り込み済み。
            var offsetX = expansionOffsetX + overlapNudgeX + minSide * (
                MaxJitterFraction * jitterStrength * itemJitterX
                + MaxRowColBiasFraction * jitterStrength * rowBiasX
                + MaxRowColBiasFraction * jitterStrength * colBiasX
                + globalDriftXFactor);
            var offsetY = expansionOffsetY + overlapNudgeY + minSide * (
                MaxJitterFraction * jitterStrength * itemJitterY
                + MaxRowColBiasFraction * jitterStrength * rowBiasY
                + MaxRowColBiasFraction * jitterStrength * colBiasY
                + globalDriftYFactor);

            var rotationDeg = rotationStrength * MaxRotationDeg * rotationFactor;
            var rotationPivotOffsetX = rotationStrength * minSide * MaxRotationPivotFraction * pivotJitterX;
            var rotationPivotOffsetY = rotationStrength * minSide * MaxRotationPivotFraction * pivotJitterY;

            // アンカー (主役) は offset / rotation を anchorDecay 倍に減衰
            // (フレーム / シャドウは変えない: 写真ボード感は維持しつつ「動きが少ない 1 枚」を作る)
            if (itemIdx == anchorIndex)
            {
                offsetX *= anchorDecay;
                offsetY *= anchorDecay;
                rotationDeg *= anchorDecay;
                rotationPivotOffsetX *= anchorDecay;
                rotationPivotOffsetY *= anchorDecay;
            }

            // 写真キャラクター (frame + shadow) は frameStrength / shadowStrength で
            // 独立にスケール。両方 1.0 のスタイルが大半だが、将来「シャドウ薄め」スタイル
            // などを足すときに分離されている方が拡張性が高い。
            var frameSidePx = (int)Math.Round(MaxFrameSidePx * frameStrength);
            var frameBottomPx = (int)Math.Round(MaxFrameBottomPx * frameStrength);
            var frameAlpha = (byte)Math.Round(255.0 * frameStrength);

            var shadowOffsetX = shadowStrength * (BaseShadowOffsetX + ShadowOffsetJitterPx * shadowOffsetXJitter);
            var shadowOffsetY = shadowStrength * (BaseShadowOffsetY + ShadowOffsetJitterPx * shadowOffsetYJitter);
            var shadowSigma = shadowStrength * Math.Max(0.0, BaseShadowSigma + ShadowSigmaJitterPx * shadowSigmaJitter);
            // alpha 揺らぎ: ±20% per-item で UI 感を消す。基準値 64 → [51, 77] くらい
            var shadowAlphaScale = 1.0 + 0.20 * shadowAlphaJitter;
            var shadowAlpha = (byte)Math.Clamp(
                Math.Round(ShadowMaxAlpha * shadowStrength * shadowAlphaScale),
                0.0, 255.0);

            items.Add(new PhotoBoardItem(
                rect,
                offsetX, offsetY,
                rotationDeg,
                rotationPivotOffsetX, rotationPivotOffsetY,
                frameSidePx, frameBottomPx, frameAlpha,
                shadowOffsetX, shadowOffsetY, shadowSigma, shadowAlpha));
        }

        // Polish pass: スタイル係数で許可されている場合のみ走る「破綻防止」ガード群。
        // 目的は自然化 (アルゴリズムが頑張りすぎて最適化臭が出る) ではなく、 孤立 /
        // 過密 / 偶然整列 / ペア化 のような構造的な配置の偶発的破綻を最小限の手数で
        // 回避すること。 アンカー placement は polish pass からも除外され「動かない 1 枚」
        // を維持する。
        if (coefs.PolishEnabled && items.Count >= 2)
        {
            ApplyPolishPass(items, rng, anchorIndex);
        }

        return items;
    }

    // ─── Polish pass: 破綻防止ガード (制約厳守: 移動量小、対象 1-2 件) ─────────

    /// <summary>孤立判定の閾値 (平均セル短辺の倍数)。</summary>
    private const double IsolationDistanceFactor = 1.4;

    /// <summary>過密判定の閾値 (平均セル短辺の倍数)。これ以内に同居していると過密。</summary>
    private const double DensityDistanceFactor = 0.7;

    /// <summary>孤立 / 過密ガードの nudge 量 (平均セル短辺の倍数)。</summary>
    private const double PolishNudgeFraction = 0.12;

    /// <summary>孤立 / 過密ガードでそれぞれ最大何件まで補正するか。</summary>
    private const int MaxPolishItemsPerGuard = 2;

    /// <summary>整列破壊で「揃いすぎ」とみなす隣接アイテム間の角度差 (度)。</summary>
    private const double AlignmentRotationThresholdDeg = 1.0;

    /// <summary>整列破壊で適用する角度補正量 (度)。</summary>
    private const double AlignmentBreakRotationDeg = 1.5;

    private static void ApplyPolishPass(List<PhotoBoardItem> items, Random rng, int anchorIndex)
    {
        var avgMinSide = 0.0;
        for (int i = 0; i < items.Count; i++)
            avgMinSide += Math.Min(items[i].BaseRect.Width, items[i].BaseRect.Height);
        avgMinSide /= items.Count;

        var nudgeMag = avgMinSide * PolishNudgeFraction;
        var isolationThreshold = avgMinSide * IsolationDistanceFactor;
        var densityThreshold = avgMinSide * DensityDistanceFactor;
        var pairDistance = avgMinSide * PairSeparationDistanceFactor;
        var pairNudge = avgMinSide * PairSeparationNudgeFraction;

        ApplyIsolationGuard(items, isolationThreshold, nudgeMag, anchorIndex);
        ApplyDensityGuard(items, densityThreshold, nudgeMag, anchorIndex);
        ApplyAlignmentBreak(items, rng, anchorIndex);
        ApplyPairSeparation(items, pairDistance, pairNudge, anchorIndex);
    }

    /// <summary>
    /// 孤立ガード: 最近傍距離が閾値を超える placement を最近傍方向に少しだけ寄せる。
    /// 完全孤立した「ぽつん」placement を見えなくする。
    /// </summary>
    private static void ApplyIsolationGuard(List<PhotoBoardItem> items, double threshold, double nudgeMag, int anchorIndex)
    {
        var positions = ComputeCenters(items);
        var candidates = new List<(int Index, int NearestIdx, double Distance)>();
        for (int i = 0; i < items.Count; i++)
        {
            if (i == anchorIndex) continue;  // アンカーは動かさない
            var (nearest, dist) = NearestNeighbor(positions, i);
            if (nearest >= 0 && dist > threshold)
                candidates.Add((i, nearest, dist));
        }
        // 距離が大きい順に最大 MaxPolishItemsPerGuard 件
        candidates.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        var take = Math.Min(MaxPolishItemsPerGuard, candidates.Count);
        for (int k = 0; k < take; k++)
        {
            var (i, nearest, _) = candidates[k];
            var dx = positions[nearest].X - positions[i].X;
            var dy = positions[nearest].Y - positions[i].Y;
            var mag = Math.Sqrt(dx * dx + dy * dy);
            if (mag < 1e-6) continue;
            items[i] = items[i] with
            {
                OffsetX = items[i].OffsetX + (dx / mag) * nudgeMag,
                OffsetY = items[i].OffsetY + (dy / mag) * nudgeMag,
            };
        }
    }

    /// <summary>
    /// 過密ガード: 同じエリアに 3 件以上 (= 自分 + 近傍 2 件) が固まる場合、最も内側の
    /// 1 件をクラスタ重心の反対方向に少しだけ逃がす。中央吸着 / 局所集中の破綻を防ぐ。
    /// </summary>
    private static void ApplyDensityGuard(List<PhotoBoardItem> items, double threshold, double nudgeMag, int anchorIndex)
    {
        var positions = ComputeCenters(items);
        var candidates = new List<(int Index, double CX, double CY, int Count)>();
        for (int i = 0; i < items.Count; i++)
        {
            if (i == anchorIndex) continue;  // アンカーは動かさない
            int count = 0;
            double cx = 0, cy = 0;
            for (int j = 0; j < items.Count; j++)
            {
                if (i == j) continue;
                var dx = positions[j].X - positions[i].X;
                var dy = positions[j].Y - positions[i].Y;
                if (Math.Sqrt(dx * dx + dy * dy) < threshold)
                {
                    count++;
                    cx += positions[j].X;
                    cy += positions[j].Y;
                }
            }
            if (count >= 2) // 自分 + 近傍 2 件 = 3 件以上のクラスタ
            {
                cx /= count;
                cy /= count;
                candidates.Add((i, cx, cy, count));
            }
        }
        // 過密度が高い順
        candidates.Sort((a, b) => b.Count.CompareTo(a.Count));
        var take = Math.Min(MaxPolishItemsPerGuard, candidates.Count);
        for (int k = 0; k < take; k++)
        {
            var (i, cx, cy, _) = candidates[k];
            var awayX = positions[i].X - cx;
            var awayY = positions[i].Y - cy;
            var mag = Math.Sqrt(awayX * awayX + awayY * awayY);
            if (mag < 1e-6) continue;
            items[i] = items[i] with
            {
                OffsetX = items[i].OffsetX + (awayX / mag) * nudgeMag,
                OffsetY = items[i].OffsetY + (awayY / mag) * nudgeMag,
            };
        }
    }

    /// <summary>
    /// 整列破壊: 角度差 &lt; 1° の連続が 3 件以上ある場合、その中の 1 件だけ ±1.5° 回転を加える。
    /// 「偶然全員が水平に揃う」ケースを破壊して写真ボード感を保つ。
    /// </summary>
    private static void ApplyAlignmentBreak(List<PhotoBoardItem> items, Random rng, int anchorIndex)
    {
        if (items.Count < 3) return;

        // 角度でソートし、連続する隣接間で角度差が閾値以下の連が 3 件以上ある最初を探す
        var indices = Enumerable.Range(0, items.Count).ToList();
        indices.Sort((a, b) => items[a].RotationDeg.CompareTo(items[b].RotationDeg));

        int runStart = 0;
        for (int i = 1; i < indices.Count; i++)
        {
            if (Math.Abs(items[indices[i]].RotationDeg - items[indices[i - 1]].RotationDeg)
                <= AlignmentRotationThresholdDeg)
            {
                if (i - runStart + 1 >= 3)
                {
                    // 連の中央 1 件に ±1.5° の補正。アンカーなら 1 つ隣に切替。
                    var midPos = (runStart + i) / 2;
                    var midIdx = indices[midPos];
                    if (midIdx == anchorIndex)
                    {
                        // アンカーの場合は連内の別の item を選択
                        if (midPos + 1 <= i) midIdx = indices[midPos + 1];
                        else if (midPos - 1 >= runStart) midIdx = indices[midPos - 1];
                        else return; // 連が 1 件しかなくアンカーを含んでいたらスキップ
                        if (midIdx == anchorIndex) return;
                    }
                    var sign = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
                    items[midIdx] = items[midIdx] with
                    {
                        RotationDeg = items[midIdx].RotationDeg + sign * AlignmentBreakRotationDeg,
                    };
                    return; // 破壊は最大 1 件
                }
            }
            else
            {
                runStart = i;
            }
        }
    }

    /// <summary>
    /// ペア分解: 距離 &lt; 0.8 × 平均セル短辺 + 角度差 &lt; 2° のペアを 1 件だけ ±10px 離す。
    /// 「同方向に並んだ 2 件 = ペア感」を緩和して「全部独立した写真」に見せる。
    /// </summary>
    private static void ApplyPairSeparation(List<PhotoBoardItem> items, double distThreshold, double nudgeMag, int anchorIndex)
    {
        if (items.Count < 2) return;
        var positions = ComputeCenters(items);

        // 1 ペアだけ処理 (制約厳守)
        for (int i = 0; i < items.Count; i++)
        {
            if (i == anchorIndex) continue;
            for (int j = i + 1; j < items.Count; j++)
            {
                if (j == anchorIndex) continue;
                var dx = positions[i].X - positions[j].X;
                var dy = positions[i].Y - positions[j].Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist >= distThreshold) continue;

                var rotDiff = Math.Abs(items[i].RotationDeg - items[j].RotationDeg);
                if (rotDiff > PairSeparationRotationThresholdDeg) continue;

                // 同方向ペア発見: i を j から離す方向に nudge
                if (dist < 1e-6) continue;
                var ux = dx / dist;
                var uy = dy / dist;
                items[i] = items[i] with
                {
                    OffsetX = items[i].OffsetX + ux * nudgeMag,
                    OffsetY = items[i].OffsetY + uy * nudgeMag,
                };
                return; // 1 ペアのみ処理
            }
        }
    }

    private static (double X, double Y)[] ComputeCenters(List<PhotoBoardItem> items)
    {
        var positions = new (double X, double Y)[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            positions[i] = (
                items[i].BaseRect.X + items[i].BaseRect.Width / 2.0 + items[i].OffsetX,
                items[i].BaseRect.Y + items[i].BaseRect.Height / 2.0 + items[i].OffsetY);
        }
        return positions;
    }

    private static (int Index, double Distance) NearestNeighbor((double X, double Y)[] positions, int self)
    {
        double minDist = double.MaxValue;
        int minIdx = -1;
        for (int j = 0; j < positions.Length; j++)
        {
            if (j == self) continue;
            var dx = positions[j].X - positions[self].X;
            var dy = positions[j].Y - positions[self].Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d < minDist) { minDist = d; minIdx = j; }
        }
        return (minIdx, minDist);
    }

    /// <summary>
    /// PhotoBoard モードの最終キャンバスに必要なマージン (4 辺それぞれに加算) を計算する。
    /// 位置オフセット + 回転対角線分の伸び + フレーム + シャドウぼかしすべての最大値を含む。
    /// </summary>
    public static int RequiredCanvasMargin(
        IReadOnlyList<PlacementBaseRect> baseRects,
        PixelSize canvas,
        PhotoBoardStyleCoefficients coefs)
    {
        ArgumentNullException.ThrowIfNull(baseRects);
        ArgumentNullException.ThrowIfNull(coefs);
        if (baseRects.Count == 0)
            return 0;

        var maxMinSide = baseRects.Max(r => Math.Min(r.Rect.Width, r.Rect.Height));
        var maxLongSide = baseRects.Max(r => Math.Max(r.Rect.Width, r.Rect.Height));

        // 位置 (jitter + row/col bias の最悪ケース合算 × jitterStrength + 全体ドリフトの最悪ケース × driftStrength)
        var maxOffset = maxMinSide * (
            (MaxJitterFraction + 2.0 * MaxRowColBiasFraction) * coefs.JitterStrength
            + MaxGlobalDriftFraction * coefs.DriftStrength);

        // 明示的重なり nudge (OverlapProbability > 0 で発火可能性、最大 OverlapNudgeFraction シフト)
        var overlapNudge = coefs.OverlapProbability > 0.0
            ? OverlapNudgeFraction * maxMinSide
            : 0.0;

        // グリッド拡張: 4 隅の placement は最大 (canvas/2) * (expansion - 1) 外側へ動く
        var expansionGrowth = Math.Max(canvas.Width, canvas.Height) / 2.0
            * Math.Max(0.0, coefs.Expansion - 1.0);

        // 回転による対角伸長分: 回転後 bbox の最大増分は (W+H)/2 * sin(θ) 程度
        var maxRotRad = coefs.RotationStrength * MaxRotationDeg * Math.PI / 180.0;
        var rotationGrowth = (maxLongSide / 2.0) * Math.Abs(Math.Sin(maxRotRad))
                           + (maxMinSide / 2.0) * Math.Abs(Math.Sin(maxRotRad));

        // フレーム + シャドウ
        var frame = MaxFrameBottomPx * coefs.FrameStrength;
        var shadow = (BaseShadowOffsetY + ShadowOffsetJitterPx
                    + 3.0 * (BaseShadowSigma + ShadowSigmaJitterPx)) * coefs.ShadowStrength;

        var growth = maxOffset + overlapNudge + expansionGrowth + rotationGrowth + frame + shadow;
        // すべての貢献が 0 のとき (= Off 係数) はマージン不要。 Normal モードと
        // 同じキャンバスサイズで出力できる。
        if (growth <= 0.0)
            return 0;
        return (int)Math.Ceiling(growth + 4.0);
    }
}
