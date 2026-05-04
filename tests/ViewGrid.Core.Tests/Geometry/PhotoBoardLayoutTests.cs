using System;
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
/// 9 係数 (<see cref="PhotoBoardStyleCoefficients"/>) を独立に切り替えて、それぞれが
/// 期待される効果を持つことを確認する。
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

    /// <summary>係数を 1 つだけ書き換えたコピーを作るヘルパ。</summary>
    private static PhotoBoardStyleCoefficients With(
        PhotoBoardStyleCoefficients src,
        double? frame = null, double? shadow = null,
        double? rotation = null, double? jitter = null,
        double? overlap = null, double? expansion = null,
        double? drift = null, double? anchorDecay = null,
        bool? polish = null) =>
        new(
            FrameStrength: frame ?? src.FrameStrength,
            ShadowStrength: shadow ?? src.ShadowStrength,
            RotationStrength: rotation ?? src.RotationStrength,
            JitterStrength: jitter ?? src.JitterStrength,
            OverlapProbability: overlap ?? src.OverlapProbability,
            Expansion: expansion ?? src.Expansion,
            DriftStrength: drift ?? src.DriftStrength,
            AnchorDecay: anchorDecay ?? src.AnchorDecay,
            PolishEnabled: polish ?? src.PolishEnabled);

    [Fact]
    public void Compute_Off_Coefficients_Yields_All_Zero_Effects()
    {
        // フレーム / シャドウすら無効な「素のグリッド」状態。
        var result = PhotoBoardLayout.Compute(
            SampleGrid2x2(), SampleCanvas, PhotoBoardStyleCoefficients.Off, seed: 42);

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
    public void RequiredCanvasMargin_Off_Coefficients_Returns_Zero()
    {
        PhotoBoardLayout.RequiredCanvasMargin(
            SampleGrid2x2(), SampleCanvas, PhotoBoardStyleCoefficients.Off)
            .Should().Be(0);
    }

    [Fact]
    public void Compute_Scattered_Max_Intensity_Stays_Within_Bounds()
    {
        // 200×200 セル (canvas 400×400)、 Scattered + intensity=1.0:
        //   factor = 2.0、 baseline (Scattered): rotation/jitter=0.9 → 1.0 (clamp)、 expansion=1.35 → 1.70 (clamp なし)、 overlap=0.40 → 0.80 (clamp)
        //   位置オフセット最大 ≒ 200×(0.05 + 2×0.10 + 0.06) = 62
        //   拡張 (4 隅 placement): |center|=100, expansion 倍率 1.70 → ±70
        //   overlap nudge (per-item 0.80 確率): 最大 ±40 (= 200×0.20)
        //   polish pass: 各 ±24 (孤立/過密) + ±10 (ペア分解)
        //   合計 OffsetX/Y 最悪ケース ≒ ±62 + ±70 + ±40 + ±24 + ±24 + ±10 = ±230
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, coefs, seed: 42);

        foreach (var item in result)
        {
            item.OffsetX.Should().BeInRange(-235.0, 235.0);
            item.OffsetY.Should().BeInRange(-235.0, 235.0);
            item.RotationDeg.Should().BeInRange(-9.5, 9.5);  // ±8 + ±1.5 (alignment break)
            item.RotationPivotOffsetX.Should().BeInRange(-36.0, 36.0);  // 200×0.18
            item.RotationPivotOffsetY.Should().BeInRange(-36.0, 36.0);
            item.FrameSidePx.Should().Be(12);
            item.FrameBottomPx.Should().Be(36);
            item.FrameAlpha.Should().Be(255);
            item.ShadowAlpha.Should().BeInRange((byte)51, (byte)77);  // 64 ± 20%
            item.ShadowOffsetX.Should().BeInRange(0.0, 4.0);
            item.ShadowOffsetY.Should().BeInRange(2.0, 6.0);
            item.ShadowSigma.Should().BeInRange(0.0, 6.0);
        }
    }

    [Fact]
    public void Compute_OverlapProbability_Zero_Has_No_Nudge()
    {
        // OverlapProbability=0 では nudge も per-item expansion variation も発火しない。
        // ナチュラルスタイル baseline (overlap=0) でこの不変条件を確認。
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 0.5);
        coefs.OverlapProbability.Should().Be(0.0);

        // 100 シードでも nudge による「片軸 > 60px」のオフセットが発生しないことを確認
        const int trials = 100;
        int largeOffsetCount = 0;
        for (int seed = 1; seed <= trials; seed++)
        {
            var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, coefs, seed);
            foreach (var item in result)
            {
                if (Math.Abs(item.OffsetX) > 60 || Math.Abs(item.OffsetY) > 60) largeOffsetCount++;
            }
        }
        largeOffsetCount.Should().Be(0,
            "OverlapProbability=0 のスタイルでは nudge は決して発火しない");
    }

    [Fact]
    public void Compute_OverlapProbability_Increases_With_Coefficient()
    {
        // OverlapProbability の効果を分離するため、他の散らかし係数を全部 0 にした
        // baseline で測定。 OverlapProbability が大きくなるほど nudge 発火数が単調に増える。
        var minimal = new PhotoBoardStyleCoefficients(
            FrameStrength: 1.0, ShadowStrength: 1.0,
            RotationStrength: 0, JitterStrength: 0,
            OverlapProbability: 0, Expansion: 1.0,
            DriftStrength: 0, AnchorDecay: 0.30, PolishEnabled: false);
        const int trials = 100;
        int CountNudgedItems(double overlapProb)
        {
            var coefs = With(minimal, overlap: overlapProb);
            int count = 0;
            for (int seed = 1; seed <= trials; seed++)
            {
                var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, coefs, seed);
                foreach (var item in result)
                {
                    // 他の貢献はすべて 0 なので、 offset > 1 は確実に nudge 由来
                    if (Math.Abs(item.OffsetX) > 1.0 || Math.Abs(item.OffsetY) > 1.0) count++;
                }
            }
            return count;
        }
        var c000 = CountNudgedItems(0.00);
        var c020 = CountNudgedItems(0.20);
        var c050 = CountNudgedItems(0.50);
        var c100 = CountNudgedItems(1.00);

        c000.Should().Be(0, "overlap=0 では決して nudge は発火しない");
        c020.Should().BeGreaterThan(c000, "overlap=0.2 で nudge が発火し始める");
        c050.Should().BeGreaterThan(c020, "overlap=0.5 で発火数がさらに増える");
        c100.Should().BeGreaterThan(c050, "overlap=1.0 で発火数が最大");
    }

    [Fact]
    public void Compute_High_Expansion_Pushes_Corner_Placements_Outward()
    {
        // expansion=1.35 (Scattered baseline) で 4 隅の placement は内部的にキャンバス
        // 中心から外側に移動する。 jitter/overlap の影響を平均で消すため 100 シードで集計。
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);

        double sumOffsetX0 = 0, sumOffsetX1 = 0, sumOffsetY0 = 0, sumOffsetY3 = 0;
        const int trials = 100;
        for (int seed = 1; seed <= trials; seed++)
        {
            var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, coefs, seed);
            sumOffsetX0 += result[0].OffsetX;  // 左上 → -X 期待
            sumOffsetX1 += result[1].OffsetX;  // 右上 → +X 期待
            sumOffsetY0 += result[0].OffsetY;  // 左上 → -Y 期待
            sumOffsetY3 += result[3].OffsetY;  // 右下 → +Y 期待
        }

        // overlap > 0 の場合 per-item で expansion 倍率がばらつき (-0.4 ～ +1.0)、
        // さらに jitter / drift / polish のノイズが乗るため、平均オフセットは小さめに収束する。
        // 安全側で ±3 を閾値とする (符号方向だけ確認、 Scattered max+intensity=1 では
        // 100 trials の平均で必ず期待方向に偏ることを担保)。
        (sumOffsetX0 / trials).Should().BeLessThan(-3.0,
            "左側 placement は拡張で左方向 (-X) に平均的にシフトするはず");
        (sumOffsetX1 / trials).Should().BeGreaterThan(3.0,
            "右側 placement は拡張で右方向 (+X) に平均的にシフトするはず");
        (sumOffsetY0 / trials).Should().BeLessThan(-3.0,
            "上側 placement は拡張で上方向 (-Y) に平均的にシフトするはず");
        (sumOffsetY3 / trials).Should().BeGreaterThan(3.0,
            "下側 placement は拡張で下方向 (+Y) に平均的にシフトするはず");
    }

    [Fact]
    public void Compute_Same_Seed_Yields_Same_Sequence()
    {
        var input = SampleGrid2x2();
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Rough, 0.5);
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 12345);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 12345);

        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Compute_Different_Seed_Yields_Different_Sequence()
    {
        var input = SampleGrid2x2();
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Rough, 0.5);
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 1);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 2);

        bool anyDifferent = a.Zip(b, (x, y) => x.OffsetX != y.OffsetX).Any(diff => diff);
        anyDifferent.Should().BeTrue();
    }

    [Fact]
    public void Compute_Empty_Input_Returns_Empty()
    {
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 0.5);
        var result = PhotoBoardLayout.Compute(
            Array.Empty<PlacementBaseRect>(), SampleCanvas, coefs, 42);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Compute_PolishEnabled_False_Skips_Pass()
    {
        // PolishEnabled=false で polish pass が走らないことを確認。 同じ係数で
        // PolishEnabled だけ true/false 切替し、結果が変わることで polish の発火を担保。
        var baseline = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);
        var withPolish = With(baseline, polish: true);
        var withoutPolish = With(baseline, polish: false);

        // 100 シードで「結果が異なる」ケースを 1 件以上確認 (polish が変えうる範囲)
        bool anyDiff = false;
        for (int seed = 1; seed <= 100; seed++)
        {
            var a = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, withPolish, seed);
            var b = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, withoutPolish, seed);
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].OffsetX != b[i].OffsetX || a[i].OffsetY != b[i].OffsetY ||
                    a[i].RotationDeg != b[i].RotationDeg)
                {
                    anyDiff = true;
                    break;
                }
            }
            if (anyDiff) break;
        }
        anyDiff.Should().BeTrue("polish 有効/無効で結果に差が出るシードが少なくとも 1 つ存在する");
    }

    [Fact]
    public void Compute_PolishPass_Preserves_Determinism()
    {
        var input = SampleGrid2x2();
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 12345);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 12345);

        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Compute_Anchor_Has_Smaller_Movement_Than_Other_Items()
    {
        // 大きい disorder + 小さい AnchorDecay で 1 件のアンカーが他より明確に動かない。
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);
        const int trials = 100;
        int trialsAnchorWasSmallest = 0;
        for (int seed = 1; seed <= trials; seed++)
        {
            var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, coefs, seed);
            var magnitudes = result.Select(item =>
                Math.Sqrt(item.OffsetX * item.OffsetX + item.OffsetY * item.OffsetY)).ToList();
            var minMag = magnitudes.Min();
            var maxMag = magnitudes.Max();
            if (minMag < maxMag * 0.5)
                trialsAnchorWasSmallest++;
        }
        trialsAnchorWasSmallest.Should().BeGreaterThan(70,
            "Scattered max でもアンカー減衰により 1 件が他より明確に小さい offset を持つはず");
    }

    [Fact]
    public void Compute_Anchor_Mechanism_Preserves_Determinism()
    {
        var input = SampleGrid2x2();
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 9999);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, coefs, seed: 9999);

        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Compute_FrameStrength_Scales_Frame_Size_Linearly()
    {
        // FrameStrength を 0.5 にすると frame サイズも 50% になる。
        // 旧二段階カーブは frameRamp = chaos/0.20 だったが、今は係数直接参照。
        var baseline = PhotoBoardStyleCoefficients.Aligned;
        var halfFrame = With(baseline, frame: 0.5);
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, halfFrame, seed: 42);

        foreach (var item in result)
        {
            item.FrameSidePx.Should().Be(6);    // 12 * 0.5
            item.FrameBottomPx.Should().Be(18); // 36 * 0.5
            item.FrameAlpha.Should().Be(128);   // 255 * 0.5 ≈ 128
        }
    }

    [Fact]
    public void Compute_Single_Item_Still_Deterministic()
    {
        var input = new[] { new PlacementBaseRect(0, 0, new PixelRect(0, 0, 100, 100)) };
        var canvas = new PixelSize(100, 100);
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);

        var a = PhotoBoardLayout.Compute(input, canvas, coefs, seed: 7);
        var b = PhotoBoardLayout.Compute(input, canvas, coefs, seed: 7);

        a.Should().HaveCount(1);
        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Compute_Different_Styles_Produce_Different_Output()
    {
        // ナチュラルとバラ撒きで visibly 異なる結果になることを担保。
        // 同じシード + 同じ入力で結果の差異を検出 (各スタイルの個性を担保)。
        var input = SampleGrid2x2();
        var natural = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 0.5);
        var scattered = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 0.5);
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, natural, seed: 42);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, scattered, seed: 42);

        // ナチュラル < バラ撒き の rotation magnitude が期待される (どこかの item で必ず差が出る)
        var maxNaturalRot = a.Max(i => Math.Abs(i.RotationDeg));
        var maxScatteredRot = b.Max(i => Math.Abs(i.RotationDeg));
        maxScatteredRot.Should().BeGreaterThan(maxNaturalRot,
            "バラ撒きスタイルの方がナチュラルスタイルより回転量が大きい");
    }
}
