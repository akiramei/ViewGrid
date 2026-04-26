using System;
using System.Collections.Generic;
using System.Linq;

namespace ViewGrid.Core.Geometry;

/// <summary>
/// グリッドの境界ハンドルドラッグで隣接 2 セルの重みを再分配する純粋関数を提供する。
/// 入力 / 出力ともに正の整数配列で、隣接 2 セルの合計重みは保たれる。
/// 出力の各値は <see cref="MaxNormalizedWeight"/> 以下に正規化される。
/// </summary>
/// <remarks>
/// 過去にこの計算ロジックは <c>GridCanvasView</c> の private static メソッドに置かれており、
/// テスト不能だったため 2 つの設計バグを実機検証で繰り返し踏んだ:
/// <list type="number">
/// <item>出力に <c>int.MaxValue</c> 上限制約がなく、GCD 縮小が効かない場合に全要素同値化 → ドラッグ不能</item>
/// <item>入力スケーリングがなく、<c>[1,1,1]</c> 状態で <c>deltaWeight</c> が 0 に丸められて変化しない</item>
/// </list>
/// テスト可能な独立クラスに切り出して、両方向の境界値で回帰防止する。
/// </remarks>
public static class WeightRedistributor
{
    /// <summary>出力配列の各要素の上限値。GCD 正規化後にこれを超える場合は divisor で割って縮小する。</summary>
    public const int MaxNormalizedWeight = 10000;

    /// <summary>
    /// 内部スケール定数。<paramref name="startWeights"/> が <c>[1,1,1]</c> のような小さい値でも、
    /// 内部計算の <c>total</c> を十分大きく保ち <c>deltaWeight</c> が 0 に丸められない粒度を確保する。
    /// </summary>
    private const long InternalDragScale = 100_000L;

    /// <summary>
    /// 境界 <paramref name="boundaryIndex"/> をドラッグした結果の新しい重み配列を返す。
    /// 入力が空・境界外・差分 0 などの不変ケースは <paramref name="startWeights"/> をそのまま返す。
    /// </summary>
    /// <param name="startWeights">押下時の重み配列。長さは少なくとも 2。</param>
    /// <param name="boundaryIndex">移動する境界のインデックス（1 以上、Count 未満）。</param>
    /// <param name="deltaPx">ドラッグの移動量。<paramref name="totalSize"/> と同じ単位。</param>
    /// <param name="totalSize">グリッド全体の対応軸の長さ（例: 表示キャンバス幅 600）。</param>
    public static int[] Redistribute(
        IReadOnlyList<int> startWeights,
        int boundaryIndex,
        double deltaPx,
        double totalSize)
    {
        ArgumentNullException.ThrowIfNull(startWeights);
        if (startWeights.Count < 2) return [.. startWeights];
        if (boundaryIndex <= 0 || boundaryIndex >= startWeights.Count)
            return [.. startWeights];
        if (totalSize <= 0) return [.. startWeights];

        // 入力スケーリング: 小さい値で deltaWeight が 0 に潰れない粒度を確保。
        // long で計算してオーバーフロー回避（int.MaxValue 級入力でも安全）。
        var weights = new long[startWeights.Count];
        for (var i = 0; i < startWeights.Count; i++)
            weights[i] = (long)Math.Max(1, startWeights[i]) * InternalDragScale;

        long total = 0;
        for (var i = 0; i < weights.Length; i++) total += weights[i];
        if (total <= 0) return [.. startWeights];

        var deltaWeight = (long)Math.Round(deltaPx / totalSize * total);
        if (deltaWeight == 0) return [.. startWeights];

        var left = weights[boundaryIndex - 1];
        var right = weights[boundaryIndex];
        var combined = left + right;

        var newLeft = Math.Clamp(left + deltaWeight, 1L, combined - 1);
        var newRight = combined - newLeft;
        weights[boundaryIndex - 1] = newLeft;
        weights[boundaryIndex] = newRight;

        return NormalizeToSmallInt(weights);
    }

    /// <summary>
    /// 占有セル群（<paramref name="occupantStart"/> から <paramref name="occupantSpan"/> 個）の幅を
    /// 「実描画矩形の内側幅」<paramref name="occupantInner"/> に縮め、左右余白
    /// <paramref name="leftPad"/> / <paramref name="rightPad"/> を隣接セルへ加算する。
    /// 隣接セルが存在しない側（占有が端列/端行）の余白は破棄され、全体合計重みが減る
    /// （= 占有列群がさらに小さく見え、画像が端に張り付く挙動）。
    /// </summary>
    /// <remarks>
    /// 入力は <c>leftPad + occupantInner + rightPad</c> ≒ 占有セル群の現在幅 を想定。
    /// この前提が崩れると比率が崩れるため、呼び出し側で保証すること。
    /// </remarks>
    public static int[] FitToOccupant(
        IReadOnlyList<int> startWeights,
        int occupantStart,
        int occupantSpan,
        long leftPad,
        long occupantInner,
        long rightPad)
    {
        ArgumentNullException.ThrowIfNull(startWeights);
        if (startWeights.Count == 0) return [.. startWeights];
        if (occupantStart < 0 || occupantStart >= startWeights.Count) return [.. startWeights];
        if (occupantSpan <= 0 || occupantStart + occupantSpan > startWeights.Count) return [.. startWeights];
        if (occupantInner <= 0) return [.. startWeights];
        if (leftPad < 0 || rightPad < 0) return [.. startWeights];
        if (leftPad == 0 && rightPad == 0) return [.. startWeights]; // 余白なし → 何もしない

        var hasLeft = occupantStart > 0;
        var hasRight = occupantStart + occupantSpan < startWeights.Count;
        if (!hasLeft && !hasRight) return [.. startWeights]; // 全列占有 → 分配先なし

        var occupantPxTotal = leftPad + occupantInner + rightPad;
        if (occupantPxTotal <= 0) return [.. startWeights];

        // 入力スケーリング: 小さい値で重み変換量が 0 に潰れないよう内部スケール。
        var weights = new long[startWeights.Count];
        for (var i = 0; i < startWeights.Count; i++)
            weights[i] = (long)Math.Max(1, startWeights[i]) * InternalDragScale;

        long occupantWeightTotal = 0;
        for (var i = occupantStart; i < occupantStart + occupantSpan; i++)
            occupantWeightTotal += weights[i];
        if (occupantWeightTotal <= 0) return [.. startWeights];

        // 占有セル群の新しい合計 = 元の合計 × (内側幅 / 元の合計幅)
        var newOccupantTotal = (long)Math.Round((double)occupantWeightTotal * occupantInner / occupantPxTotal);
        if (newOccupantTotal < occupantSpan) newOccupantTotal = occupantSpan; // 各セル最小 1 を保証

        // 余白に対応する重み量（元の占有合計重みからの按分）
        var leftPadWeight = (long)Math.Round((double)occupantWeightTotal * leftPad / occupantPxTotal);
        var rightPadWeight = (long)Math.Round((double)occupantWeightTotal * rightPad / occupantPxTotal);

        // 占有セル群を内部比率維持でスケール
        for (var i = occupantStart; i < occupantStart + occupantSpan; i++)
            weights[i] = (long)Math.Round((double)weights[i] * newOccupantTotal / occupantWeightTotal);

        if (hasLeft)
            weights[occupantStart - 1] += leftPadWeight;
        if (hasRight)
            weights[occupantStart + occupantSpan] += rightPadWeight;

        for (var i = 0; i < weights.Length; i++)
            if (weights[i] < 1) weights[i] = 1;

        return NormalizeToSmallInt(weights);
    }

    /// <summary>
    /// long の重み配列を、最大値が <see cref="MaxNormalizedWeight"/> 以下の小さい int 配列に正規化する。
    /// まず GCD で縮小し、それでも大きい場合は divisor で割ってスケールダウン（最小値は 1 に clamp）。
    /// </summary>
    private static int[] NormalizeToSmallInt(long[] weights)
    {
        var gcd = weights.Aggregate(GcdLong);
        if (gcd <= 0) gcd = 1;
        var normalized = weights.Select(w => w / gcd).ToArray();

        var max = normalized.Max();
        if (max > MaxNormalizedWeight)
        {
            var divisor = (double)max / MaxNormalizedWeight;
            normalized = normalized.Select(w => Math.Max(1L, (long)Math.Round(w / divisor))).ToArray();
        }

        return normalized.Select(w => (int)Math.Clamp(w, 1L, int.MaxValue)).ToArray();
    }

    private static long GcdLong(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a == 0 ? 1L : a;
    }
}
