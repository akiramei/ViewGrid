using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.UseCases;
using Xunit;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// <see cref="PhotoBoardLayout"/> の境界値テスト。
/// 主に「chaos=0 で全効果ゼロ」「同シードで決定論的」「列・行バイアスが共有される」
/// の 3 つの不変条件を担保する。
/// </summary>
public sealed class PhotoBoardLayoutTests
{
    private static PlacementBaseRect[] SampleGrid2x2() => new[]
    {
        new PlacementBaseRect(0, 0, new PixelRect(0,   0,   200, 200)),
        new PlacementBaseRect(0, 1, new PixelRect(200, 0,   200, 200)),
        new PlacementBaseRect(1, 0, new PixelRect(0,   200, 200, 200)),
        new PlacementBaseRect(1, 1, new PixelRect(200, 200, 200, 200)),
    };

    [Fact]
    public void Compute_Chaos_Zero_Yields_All_Zero_Effects()
    {
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), chaos: 0.0, seed: 42);

        result.Should().HaveCount(4);
        foreach (var item in result)
        {
            item.OffsetX.Should().Be(0.0);
            item.OffsetY.Should().Be(0.0);
            item.RotationDeg.Should().Be(0.0);
            item.RotationPivotOffsetX.Should().Be(0.0);
            item.RotationPivotOffsetY.Should().Be(0.0);
            item.FrameSidePx.Should().Be(0);
            item.FrameBottomPx.Should().Be(0);
            item.FrameAlpha.Should().Be(0);
            item.ShadowOffsetX.Should().Be(0.0);
            item.ShadowOffsetY.Should().Be(0.0);
            item.ShadowSigma.Should().Be(0.0);
            item.ShadowAlpha.Should().Be(0);
        }
    }

    [Fact]
    public void RequiredCanvasMargin_Chaos_Zero_Returns_Zero()
    {
        PhotoBoardLayout.RequiredCanvasMargin(SampleGrid2x2(), chaos: 0.0)
            .Should().Be(0);
    }

    [Fact]
    public void Compute_Chaos_One_Stays_Within_Bounds()
    {
        // 200×200 セル, chaos=1:
        //   jitter ≤ 200×0.05 = 10
        //   rowBias ≤ 200×0.10 = 20
        //   colBias ≤ 200×0.10 = 20
        //   合計位置オフセット最大 = ±50
        // 回転 ≤ ±8 度
        // フレーム = 12 / 36
        // シャドウ alpha = 64
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), chaos: 1.0, seed: 42);

        foreach (var item in result)
        {
            item.OffsetX.Should().BeInRange(-50.0, 50.0);
            item.OffsetY.Should().BeInRange(-50.0, 50.0);
            item.RotationDeg.Should().BeInRange(-8.0, 8.0);
            item.RotationPivotOffsetX.Should().BeInRange(-20.0, 20.0);
            item.RotationPivotOffsetY.Should().BeInRange(-20.0, 20.0);
            item.FrameSidePx.Should().Be(12);
            item.FrameBottomPx.Should().Be(36);
            item.FrameAlpha.Should().Be(255);
            item.ShadowAlpha.Should().Be(64);
            item.ShadowOffsetX.Should().BeInRange(0.0, 4.0);  // base 2 ± 1
            item.ShadowOffsetY.Should().BeInRange(2.0, 6.0);  // base 4 ± 1
            item.ShadowSigma.Should().BeInRange(0.0, 5.0);    // base 4 ± 0.5 (Max で clamp)
        }
    }

    [Fact]
    public void Compute_Same_Seed_Yields_Same_Sequence()
    {
        var input = SampleGrid2x2();
        var a = PhotoBoardLayout.Compute(input, chaos: 0.5, seed: 12345);
        var b = PhotoBoardLayout.Compute(input, chaos: 0.5, seed: 12345);

        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Compute_Different_Seed_Yields_Different_Sequence()
    {
        var input = SampleGrid2x2();
        var a = PhotoBoardLayout.Compute(input, chaos: 0.5, seed: 1);
        var b = PhotoBoardLayout.Compute(input, chaos: 0.5, seed: 2);

        // 少なくとも 1 つの item で OffsetX が異なる
        bool anyDifferent = a.Zip(b, (x, y) => x.OffsetX != y.OffsetX).Any(diff => diff);
        anyDifferent.Should().BeTrue();
    }

    [Fact]
    public void Compute_Same_Row_Items_Share_Row_Bias_Direction()
    {
        // 同一行 (row=0) の 2 件で rowBias 寄与が同方向に動くことを確認する。
        // per-item ジッターは独立だが、rowBias は共有される。
        // 検証方針: 多数のシードでサンプリングし、同行のオフセット相関 > 単一の閾値 を確認。
        // (シード一発では確率的に満たさないことがあるので 100 シード平均で検証)
        var input = new[]
        {
            new PlacementBaseRect(0, 0, new PixelRect(0,   0, 200, 200)),
            new PlacementBaseRect(0, 1, new PixelRect(200, 0, 200, 200)),
        };

        int sameSignCount = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            var result = PhotoBoardLayout.Compute(input, chaos: 1.0, seed: seed);
            // rowBias の符号が同じ → 両方の OffsetX が同方向に偏る確率が高い
            // (per-item ジッターより rowBias + colBias 合算が大きいので大半は同符号)
            if ((result[0].OffsetX > 0) == (result[1].OffsetX > 0))
                sameSignCount++;
        }

        // 完全独立なら期待値 50%、行バイアス共有なら 70% 以上を見込む
        sameSignCount.Should().BeGreaterThan(60,
            "同じ行に属する placement は rowBias を共有するため、オフセット方向が高確率で揃う");
    }

    [Fact]
    public void Compute_Empty_Input_Returns_Empty()
    {
        var result = PhotoBoardLayout.Compute(System.Array.Empty<PlacementBaseRect>(), 0.5, 42);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Compute_Single_Item_Still_Deterministic()
    {
        var input = new[] { new PlacementBaseRect(0, 0, new PixelRect(0, 0, 100, 100)) };

        var a = PhotoBoardLayout.Compute(input, chaos: 1.0, seed: 7);
        var b = PhotoBoardLayout.Compute(input, chaos: 1.0, seed: 7);

        a.Should().HaveCount(1);
        a.Should().BeEquivalentTo(b);
    }
}
