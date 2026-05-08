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
    /// 「画像の描画幅」<paramref name="occupantInner"/> に合わせて縮小 / 拡大し、
    /// <paramref name="leftPad"/> / <paramref name="rightPad"/> (signed) を隣接セルへ加減算する。
    /// pad が正値: cell が image より大きい → 余白を隣接セルへ加算 (= cell 縮小)。
    /// pad が負値: image が cell より大きく overflow → 隣接セルから減算 (= cell 拡大)。
    /// 占有が端列 / 端行で片側に隣接セルが無い場合、 反対側の隣接セルが両 pad を引き受ける。
    /// 両側とも隣接無し = 単一セルグリッド時のみ no-op。 total 重みは保たれ、
    /// 1 回のフィットで occupant が確実に <paramref name="occupantInner"/> 幅に収束する。
    /// </summary>
    /// <remarks>
    /// 入力は <c>leftPad + occupantInner + rightPad = </c> 占有セル群の現在幅 を想定 (signed)。
    /// この前提が崩れると比率が崩れるため、呼び出し側で保証すること。
    /// </remarks>
    public static int[] FitToOccupant(
        IReadOnlyList<int> startWeights,
        int occupantStart,
        int occupantSpan,
        long leftPad,
        long occupantInner,
        long rightPad)
        => FitToOccupant(startWeights, occupantStart, occupantSpan, leftPad, occupantInner, rightPad, locked: null);

    /// <summary>
    /// ロック配列付きオーバーロード。<paramref name="locked"/> が <c>true</c> のセルは
    /// 重みが変動せず、左右の余白分配でも「飛ばして」次のアンロックセルに分配する
    /// （連続したロックセルがあれば、その向こう側の最初のアンロックセルに分配）。
    /// 占有セル群のいずれかがロックされている場合はフィット対象外（入力をそのまま返す）。
    /// <paramref name="locked"/> が null または要素数不一致の場合は全アンロック扱い。
    /// </summary>
    public static int[] FitToOccupant(
        IReadOnlyList<int> startWeights,
        int occupantStart,
        int occupantSpan,
        long leftPad,
        long occupantInner,
        long rightPad,
        IReadOnlyList<bool>? locked)
    {
        ArgumentNullException.ThrowIfNull(startWeights);
        if (ShouldSkipFit(startWeights, occupantStart, occupantSpan, leftPad, occupantInner, rightPad, out var occupantPxTotal))
            return [.. startWeights];

        var hasLockArray = locked is not null && locked.Count == startWeights.Count;

        // 占有セル群のいずれかがロックされていればフィット対象外
        if (IsAnyOccupantLocked(locked, hasLockArray, occupantStart, occupantSpan))
            return [.. startWeights];

        // 左隣の最初のアンロックセルを探す（ロック中はスキップ）
        var leftNeighbor = FindUnlockedNeighbor(
            locked, hasLockArray, occupantStart - 1, step: -1, count: startWeights.Count);

        // 右隣の最初のアンロックセルを探す
        var rightNeighbor = FindUnlockedNeighbor(
            locked, hasLockArray, occupantStart + occupantSpan, step: +1, count: startWeights.Count);

        if (leftNeighbor is null && rightNeighbor is null) return [.. startWeights]; // 分配先なし

        // 入力スケーリング: 小さい値で重み変換量が 0 に潰れないよう内部スケール。
        var weights = new long[startWeights.Count];
        for (var i = 0; i < startWeights.Count; i++)
            weights[i] = (long)Math.Max(1, startWeights[i]) * InternalDragScale;

        long occupantWeightTotal = 0;
        for (var i = occupantStart; i < occupantStart + occupantSpan; i++)
            occupantWeightTotal += weights[i];
        if (occupantWeightTotal <= 0) return [.. startWeights];

        // 占有セル群の新しい合計 = 元の合計 × (内側幅 / 元の合計幅)。 各セル最小 1 を保証するため
        // occupantSpan 以上にクランプする。
        var newOccupantTotal = Math.Max(
            occupantSpan,
            (long)Math.Round((double)occupantWeightTotal * occupantInner / occupantPxTotal));

        // 余白に対応する重み量（元の占有合計重みからの按分）
        var leftPadWeight = (long)Math.Round((double)occupantWeightTotal * leftPad / occupantPxTotal);
        var rightPadWeight = (long)Math.Round((double)occupantWeightTotal * rightPad / occupantPxTotal);

        // 占有セル群を内部比率維持でスケール
        for (var i = occupantStart; i < occupantStart + occupantSpan; i++)
            weights[i] = (long)Math.Round((double)weights[i] * newOccupantTotal / occupantWeightTotal);

        DistributePadWeights(weights, leftNeighbor, rightNeighbor, leftPadWeight, rightPadWeight);

        for (var i = 0; i < weights.Length; i++)
            if (weights[i] < 1) weights[i] = 1;

        return NormalizeToSmallInt(weights);
    }

    /// <summary>
    /// 入力が範囲外 / no-op (余白なし含む) のとき <c>true</c> を返し、 fit を skip すべきと判定する。
    /// 同時に <paramref name="occupantPxTotal"/> (占有セル群の元幅 px、 leftPad+inner+rightPad) を出力する。
    /// signed pad (負値 = 画像 overflow による cell 拡大要求) を許容するため、 個別範囲ではなく合計値で判定。
    /// </summary>
    private static bool ShouldSkipFit(
        IReadOnlyList<int> startWeights,
        int occupantStart, int occupantSpan,
        long leftPad, long occupantInner, long rightPad,
        out long occupantPxTotal)
    {
        occupantPxTotal = leftPad + occupantInner + rightPad;
        return startWeights.Count == 0
            || occupantStart < 0
            || occupantStart >= startWeights.Count
            || occupantSpan <= 0
            || occupantStart + occupantSpan > startWeights.Count
            || occupantInner <= 0
            || occupantPxTotal <= 0
            || (leftPad == 0 && rightPad == 0); // 余白なし → 何もしない
    }

    /// <summary>
    /// 余白重み (leftPad / rightPad に対応する重み) を隣接アンロックセルに分配する。
    /// 端列 (片側に隣接無し) は反対側に両方の余白を寄せる。 こうしないと leftPad / rightPad が
    /// 「破棄」 されて全体重みが減り、 占有セル群が <c>occupantInner</c> ぴったりにならず、
    /// ユーザーが何度もクリックする羽目になる (旧版で 3 回かけて収束していた症状)。
    /// </summary>
    private static void DistributePadWeights(
        long[] weights, int? leftNeighbor, int? rightNeighbor,
        long leftPadWeight, long rightPadWeight)
    {
        if (leftNeighbor is { } li && rightNeighbor is { } ri)
        {
            weights[li] += leftPadWeight;
            weights[ri] += rightPadWeight;
        }
        else if (leftNeighbor is { } liOnly)
        {
            // 占有が rightmost。 rightPad の行き先が無いので leftNeighbor に両方寄せる。
            weights[liOnly] += leftPadWeight + rightPadWeight;
        }
        else if (rightNeighbor is { } riOnly)
        {
            // 占有が leftmost。 leftPad の行き先が無いので rightNeighbor に両方寄せる。
            weights[riOnly] += leftPadWeight + rightPadWeight;
        }
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

    private static bool IsAnyOccupantLocked(
        IReadOnlyList<bool>? locked, bool hasLockArray, int occupantStart, int occupantSpan)
    {
        if (!hasLockArray) return false;
        for (var i = occupantStart; i < occupantStart + occupantSpan; i++)
            if (locked![i]) return true;
        return false;
    }

    /// <summary>
    /// <paramref name="from"/> から <paramref name="step"/> 方向 (+1 または -1) に進み、
    /// ロックされていない最初のインデックスを返す。範囲外に出たら null。
    /// </summary>
    private static int? FindUnlockedNeighbor(
        IReadOnlyList<bool>? locked, bool hasLockArray, int from, int step, int count)
    {
        for (var i = from; i >= 0 && i < count; i += step)
        {
            if (!hasLockArray || !locked![i]) return i;
        }
        return null;
    }
}
