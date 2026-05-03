using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ViewGrid.Core.Entities;
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
    private static readonly PixelSize SampleCanvas = new(400, 400);

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
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 0.0, seed: 42);

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
        PhotoBoardLayout.RequiredCanvasMargin(SampleGrid2x2(), SampleCanvas, chaos: 0.0)
            .Should().Be(0);
    }

    [Fact]
    public void Compute_Chaos_One_Stays_Within_Bounds()
    {
        // 200×200 セル (canvas 400×400), chaos=1:
        //   jitter ≤ 200×0.05 = 10
        //   rowBias ≤ 200×0.10 = 20
        //   colBias ≤ 200×0.10 = 20
        //   位置オフセット小計 = ±50
        // 拡張 (4 隅 placement): |cellCenter - canvasCenter|=100, MaxExpansionFactor=0.40
        //   → 拡張オフセット = ±40
        // 合計 OffsetX/Y = ±90
        // 回転 ≤ ±8 度
        // フレーム = 12 / 36
        // シャドウ alpha = 64
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 1.0, seed: 42);

        foreach (var item in result)
        {
            item.OffsetX.Should().BeInRange(-90.0, 90.0);
            item.OffsetY.Should().BeInRange(-90.0, 90.0);
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
    public void Compute_Chaos_One_Pushes_Corner_Placements_Outward()
    {
        // chaos=1 時、4 隅の placement は内部的にキャンバス中心から外側 (拡張) に動く。
        // ジッターを含んだ最終オフセットでも、平均としては拡張方向に偏る。
        // SampleGrid2x2 (canvas 400×400, 各 placement 200×200) では:
        //   placement (0,0) center=(100,100): canvas center (200,200) から左上方向 (-X, -Y)
        //   placement (0,1) center=(300,100): 右上 (+X, -Y)
        //   placement (1,0) center=(100,300): 左下 (-X, +Y)
        //   placement (1,1) center=(300,300): 右下 (+X, +Y)
        // 100 シードを平均すると jitter/bias は 0 に収束、拡張オフセット ±40 が残る。
        var input = SampleGrid2x2();

        double sumOffsetX0 = 0, sumOffsetX1 = 0, sumOffsetY0 = 0, sumOffsetY3 = 0;
        const int trials = 100;
        for (int seed = 1; seed <= trials; seed++)
        {
            var result = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 1.0, seed: seed);
            sumOffsetX0 += result[0].OffsetX;  // 左側 → 平均 -40 期待
            sumOffsetX1 += result[1].OffsetX;  // 右側 → 平均 +40 期待
            sumOffsetY0 += result[0].OffsetY;  // 上側 → 平均 -40 期待
            sumOffsetY3 += result[3].OffsetY;  // 下側 → 平均 +40 期待
        }

        // 平均が拡張方向 (left/up=負、right/down=正) に偏る
        (sumOffsetX0 / trials).Should().BeLessThan(-20.0,
            "左側 placement は拡張で左方向 (-X) にシフトするはず");
        (sumOffsetX1 / trials).Should().BeGreaterThan(20.0,
            "右側 placement は拡張で右方向 (+X) にシフトするはず");
        (sumOffsetY0 / trials).Should().BeLessThan(-20.0,
            "上側 placement は拡張で上方向 (-Y) にシフトするはず");
        (sumOffsetY3 / trials).Should().BeGreaterThan(20.0,
            "下側 placement は拡張で下方向 (+Y) にシフトするはず");
    }

    [Fact]
    public void Compute_Same_Seed_Yields_Same_Sequence()
    {
        var input = SampleGrid2x2();
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 0.5, seed: 12345);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 0.5, seed: 12345);

        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Compute_Different_Seed_Yields_Different_Sequence()
    {
        var input = SampleGrid2x2();
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 0.5, seed: 1);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 0.5, seed: 2);

        // 少なくとも 1 つの item で OffsetX が異なる
        bool anyDifferent = a.Zip(b, (x, y) => x.OffsetX != y.OffsetX).Any(diff => diff);
        anyDifferent.Should().BeTrue();
    }

    [Fact]
    public void Compute_Empty_Input_Returns_Empty()
    {
        var result = PhotoBoardLayout.Compute(System.Array.Empty<PlacementBaseRect>(), SampleCanvas, 0.5, 42);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Compute_TwoStage_Curve_Frame_Saturates_Before_Disorder_Starts()
    {
        // 二段階カーブの境界 (FrameRampThreshold=0.20) で
        //   - フレーム / シャドウ: 100% (frameRamp = 1)
        //   - 回転 / ジッター: 0% (disorderRamp = 0)
        // となることを確認。chaos の中間値に「明確な写真風」を出現させる UX 契約。
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 0.20, seed: 42);

        foreach (var item in result)
        {
            // 散らかしは 0
            item.OffsetX.Should().Be(0.0);
            item.OffsetY.Should().Be(0.0);
            item.RotationDeg.Should().Be(0.0);
            item.RotationPivotOffsetX.Should().Be(0.0);
            item.RotationPivotOffsetY.Should().Be(0.0);
            // フレーム / シャドウは飽和
            item.FrameSidePx.Should().Be(12);
            item.FrameBottomPx.Should().Be(36);
            item.FrameAlpha.Should().Be(255);
            item.ShadowAlpha.Should().Be(64);
        }
    }

    [Fact]
    public void Compute_Below_FrameRampThreshold_Has_Partial_Frame_No_Disorder()
    {
        // chaos=0.10 (FrameRampThreshold=0.20 の半分) では:
        //   - frameRamp = 0.5 → フレーム厚 / アルファ 50%
        //   - disorderRamp = 0 → 回転 / ジッター 0
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 0.10, seed: 42);

        foreach (var item in result)
        {
            item.OffsetX.Should().Be(0.0);
            item.RotationDeg.Should().Be(0.0);
            item.FrameSidePx.Should().Be(6);   // 12 * 0.5
            item.FrameBottomPx.Should().Be(18); // 36 * 0.5
            item.FrameAlpha.Should().Be(128);   // 255 * 0.5 ≈ 128
        }
    }

    [Fact]
    public void Compute_Single_Item_Still_Deterministic()
    {
        var input = new[] { new PlacementBaseRect(0, 0, new PixelRect(0, 0, 100, 100)) };
        var canvas = new PixelSize(100, 100);

        var a = PhotoBoardLayout.Compute(input, canvas, chaos: 1.0, seed: 7);
        var b = PhotoBoardLayout.Compute(input, canvas, chaos: 1.0, seed: 7);

        a.Should().HaveCount(1);
        a.Should().BeEquivalentTo(b);
    }
}
