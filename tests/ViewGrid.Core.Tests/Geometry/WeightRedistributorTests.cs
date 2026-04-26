using System;
using FluentAssertions;
using ViewGrid.Core.Geometry;
using Xunit;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// <see cref="WeightRedistributor.Redistribute"/> の境界値テスト。
/// 過去に実機検証で繰り返し踏んだ 2 つのバグ（出力 int.MaxValue 暴走 / 入力 [1,1,1] で
/// deltaWeight が 0 に潰れる）を含めて回帰防止する。
/// </summary>
public sealed class WeightRedistributorTests
{
    private const double TotalSize = 600.0; // GridCanvasView.CanvasFixedSize と同値

    /// <summary>
    /// 入力スケール不足バグの回帰防止。新規グリッド <c>[1,1,1]</c> を含む小さい値・
    /// 普通の値・大きい値・<see cref="int.MaxValue"/> 級まで、適度な delta で必ず
    /// 結果が start と異なること。
    /// </summary>
    [Theory]
    [InlineData(1, 1, 1)]                                          // 新規グリッド: 旧実装はこれで凍結した
    [InlineData(100, 200, 300)]
    [InlineData(10000, 10000, 10000)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]         // 暴走バグ後に到達した状態
    public void Redistribute_ResponsiveForReasonableDelta(int a, int b, int c)
    {
        var start = new[] { a, b, c };
        var result = WeightRedistributor.Redistribute(start, boundaryIndex: 1, deltaPx: 70, TotalSize);
        result.Should().NotEqual(start, $"start=[{a},{b},{c}] で deltaPx=70 のドラッグは必ず変化すべき");
    }

    /// <summary>
    /// 出力暴走バグの回帰防止。<see cref="int.MaxValue"/> 入力でも、出力は必ず
    /// <see cref="WeightRedistributor.MaxNormalizedWeight"/> 以下に正規化される。
    /// </summary>
    [Fact]
    public void Redistribute_NeverExceedsMaxNormalizedWeight()
    {
        var start = new[] { int.MaxValue, int.MaxValue, int.MaxValue };
        var result = WeightRedistributor.Redistribute(start, 1, 70, TotalSize);
        result.Max().Should().BeLessThanOrEqualTo(WeightRedistributor.MaxNormalizedWeight);
    }

    /// <summary>
    /// 隣接 2 セルの合計重みが他セルとの比率として保たれる（再分配は隣接 2 セル間でのみ）。
    /// GCD/divisor 正規化の誤差を許容するため 0.02 のマージンを取る。
    /// </summary>
    [Fact]
    public void Redistribute_PreservesAdjacentCombinedRatio()
    {
        var start = new[] { 100, 200, 300 };
        var result = WeightRedistributor.Redistribute(start, 1, 50, TotalSize);

        var combinedBefore = (double)(start[0] + start[1]);
        var totalBefore = (double)(start[0] + start[1] + start[2]);
        var ratioBefore = combinedBefore / totalBefore;

        var combinedAfter = (double)(result[0] + result[1]);
        var totalAfter = (double)(result[0] + result[1] + result[2]);
        var ratioAfter = combinedAfter / totalAfter;

        Math.Abs(ratioBefore - ratioAfter).Should().BeLessThan(0.02);
    }

    /// <summary>
    /// 大きな delta を与えても各重みは最小 1 に clamp され、ゼロや負にならない。
    /// </summary>
    [Fact]
    public void Redistribute_AllResultsAreAtLeastOne()
    {
        var start = new[] { 1, 1, 1 };
        var result = WeightRedistributor.Redistribute(start, 1, 1000, TotalSize); // 極端なドラッグ
        result.Should().AllSatisfy(w => w.Should().BeGreaterThanOrEqualTo(1));
    }

    /// <summary>delta = 0 では入力をそのまま返す。</summary>
    [Fact]
    public void Redistribute_ZeroDelta_ReturnsStartUnchanged()
    {
        var start = new[] { 100, 200, 300 };
        var result = WeightRedistributor.Redistribute(start, 1, 0, TotalSize);
        result.Should().Equal(start);
    }

    /// <summary>境界外のインデックスは入力をそのまま返す。</summary>
    [Fact]
    public void Redistribute_OutOfRangeBoundaryIndex_ReturnsStartUnchanged()
    {
        var start = new[] { 100, 200, 300 };
        WeightRedistributor.Redistribute(start, 0, 50, TotalSize).Should().Equal(start);
        WeightRedistributor.Redistribute(start, 3, 50, TotalSize).Should().Equal(start);
    }

    /// <summary>2 セル（境界 1 つ）でも変化が出る。</summary>
    [Fact]
    public void Redistribute_TwoElement_ChangesUniformPair()
    {
        var start = new[] { 1, 1 };
        var result = WeightRedistributor.Redistribute(start, 1, 100, TotalSize);
        result.Should().NotEqual(start);
        (result[0] + result[1]).Should().BeGreaterThan(0);
        result[0].Should().BeGreaterThanOrEqualTo(1);
        result[1].Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>長さ 1 以下の入力は入力をそのまま返す。</summary>
    [Fact]
    public void Redistribute_TooShortInput_ReturnsStartUnchanged()
    {
        WeightRedistributor.Redistribute(Array.Empty<int>(), 1, 50, TotalSize).Should().BeEmpty();
        var single = new[] { 5 };
        WeightRedistributor.Redistribute(single, 1, 50, TotalSize).Should().Equal(single);
    }

    /// <summary>totalSize が 0 以下なら入力をそのまま返す（ゼロ除算回避）。</summary>
    [Fact]
    public void Redistribute_NonPositiveTotalSize_ReturnsStartUnchanged()
    {
        var start = new[] { 100, 200, 300 };
        WeightRedistributor.Redistribute(start, 1, 50, 0).Should().Equal(start);
        WeightRedistributor.Redistribute(start, 1, 50, -1).Should().Equal(start);
    }

    // ----- FitToOccupant の境界値テスト -----

    /// <summary>
    /// 通常ケース: 3 列均等 [1,1,1] (各 200px / 600px キャンバス)、中央列を占有、
    /// 画像は中央列内に幅 100px (左右 50px ずつ余白) → 占有列幅が画像幅に縮み、左右隣に均等分配。
    /// 重みは相対比率なので、絶対値ではなく合計に対する比率で検証する。
    /// </summary>
    [Fact]
    public void FitToOccupant_CenterCellWithSymmetricPadding_DistributesEvenlyToNeighbors()
    {
        var start = new[] { 1, 1, 1 };
        // 中央列幅 200, 余白 50/100/50
        var result = WeightRedistributor.FitToOccupant(
            start, occupantStart: 1, occupantSpan: 1,
            leftPad: 50, occupantInner: 100, rightPad: 50);

        var resultSum = (double)result.Sum();
        var midRatio = result[1] / resultSum;

        // 中央列の比率は 100/600 = 1/6 ≒ 0.167
        midRatio.Should().BeApproximately(100.0 / 600.0, 0.02);
        // 中央が元の 1/3 から縮む
        midRatio.Should().BeLessThan(1.0 / 3.0);
        // 対称性: 左右の余白は等量なので結果も対称
        result[0].Should().Be(result[2]);
    }

    /// <summary>
    /// 最左列が占有の場合、左隣がないので leftPad は破棄。右隣には rightPad を加算。
    /// 結果として全体合計重みが減り、他の列が相対的に大きく見える。
    /// </summary>
    [Fact]
    public void FitToOccupant_LeftmostOccupant_DiscardsLeftPad()
    {
        var start = new[] { 1, 1, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, occupantStart: 0, occupantSpan: 1,
            leftPad: 50, occupantInner: 100, rightPad: 50);

        // 元 200/200/200 → 列 0 内側 100、列 1 (+rightPad) 250、列 2 そのまま 200
        // 合計 550 px（leftPad の 50 px 分は破棄）
        var sum = (double)result.Sum();
        var col0Ratio = result[0] / sum;
        var col1Ratio = result[1] / sum;
        var col2Ratio = result[2] / sum;

        col0Ratio.Should().BeApproximately(100.0 / 550.0, 0.02);
        col1Ratio.Should().BeApproximately(250.0 / 550.0, 0.02);
        col2Ratio.Should().BeApproximately(200.0 / 550.0, 0.02);
        // 列 0 比率は元 1/3 から縮む
        col0Ratio.Should().BeLessThan(1.0 / 3.0);
    }

    /// <summary>
    /// 最右列が占有の場合は対称。rightPad は破棄、leftPad は左隣に加算。
    /// </summary>
    [Fact]
    public void FitToOccupant_RightmostOccupant_DiscardsRightPad()
    {
        var start = new[] { 1, 1, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, occupantStart: 2, occupantSpan: 1,
            leftPad: 50, occupantInner: 100, rightPad: 50);

        var sum = (double)result.Sum();
        var col2Ratio = result[2] / sum;
        var col1Ratio = result[1] / sum;
        var col0Ratio = result[0] / sum;

        col2Ratio.Should().BeApproximately(100.0 / 550.0, 0.02);
        col1Ratio.Should().BeApproximately(250.0 / 550.0, 0.02);
        col0Ratio.Should().BeApproximately(200.0 / 550.0, 0.02);
        col2Ratio.Should().BeLessThan(1.0 / 3.0);
    }

    /// <summary>
    /// 占有 N×M (列 1-2 が占有) で、占有列群の内部比率は維持してスケール。
    /// </summary>
    [Fact]
    public void FitToOccupant_MultiCellOccupant_PreservesInternalRatio()
    {
        // 元: 4 列 [3, 4, 2, 1]、占有 [4, 2] (内部比 2:1)
        var start = new[] { 3, 4, 2, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, occupantStart: 1, occupantSpan: 2,
            leftPad: 30, occupantInner: 60, rightPad: 30);

        // 内部比 2:1 が維持されること（result[1] : result[2] ≒ 2 : 1）
        var ratio = (double)result[1] / result[2];
        ratio.Should().BeApproximately(2.0, 0.1);
    }

    /// <summary>余白 0（Cover/Fill 等）はアクション無効化、入力をそのまま返す。</summary>
    [Fact]
    public void FitToOccupant_NoPadding_ReturnsStartUnchanged()
    {
        var start = new[] { 2, 3, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, 1, 1, leftPad: 0, occupantInner: 100, rightPad: 0);
        result.Should().Equal(start);
    }

    /// <summary>占有の内側幅が 0 ならアクション無効。</summary>
    [Fact]
    public void FitToOccupant_ZeroInner_ReturnsStartUnchanged()
    {
        var start = new[] { 1, 1, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, 1, 1, leftPad: 50, occupantInner: 0, rightPad: 50);
        result.Should().Equal(start);
    }

    /// <summary>全列占有（左右隣どちらもない）はアクション無効。</summary>
    [Fact]
    public void FitToOccupant_FullSpanOccupant_ReturnsStartUnchanged()
    {
        var start = new[] { 1, 1, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, 0, 3, leftPad: 50, occupantInner: 100, rightPad: 50);
        result.Should().Equal(start);
    }

    /// <summary>境界外インデックスは入力をそのまま返す。</summary>
    [Fact]
    public void FitToOccupant_OutOfRangeOccupant_ReturnsStartUnchanged()
    {
        var start = new[] { 1, 1, 1 };
        WeightRedistributor.FitToOccupant(start, -1, 1, 10, 80, 10).Should().Equal(start);
        WeightRedistributor.FitToOccupant(start, 3, 1, 10, 80, 10).Should().Equal(start);
        WeightRedistributor.FitToOccupant(start, 0, 0, 10, 80, 10).Should().Equal(start);
        WeightRedistributor.FitToOccupant(start, 2, 2, 10, 80, 10).Should().Equal(start);
    }

    /// <summary>結果の重みは <see cref="WeightRedistributor.MaxNormalizedWeight"/> 以下。</summary>
    [Fact]
    public void FitToOccupant_NeverExceedsMaxNormalizedWeight()
    {
        var start = new[] { int.MaxValue, int.MaxValue, int.MaxValue };
        var result = WeightRedistributor.FitToOccupant(
            start, 1, 1, leftPad: 50, occupantInner: 100, rightPad: 50);
        result.Max().Should().BeLessThanOrEqualTo(WeightRedistributor.MaxNormalizedWeight);
        result.Should().AllSatisfy(w => w.Should().BeGreaterThanOrEqualTo(1));
    }

    /// <summary>
    /// 新規グリッド <c>[1,1,1]</c> でも内部スケール不足によるバグが起きない（Redistribute 同じ理由）。
    /// </summary>
    [Fact]
    public void FitToOccupant_TinyStartWeights_StillResponds()
    {
        var start = new[] { 1, 1, 1 };
        var result = WeightRedistributor.FitToOccupant(
            start, 1, 1, leftPad: 30, occupantInner: 140, rightPad: 30);
        result.Should().NotEqual(start);
    }
}
