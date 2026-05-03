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
        //   globalDrift ≤ 200×0.06 = 12 (全体ドリフト)
        //   位置オフセット小計 = ±62
        // 拡張 (4 隅 placement): |cellCenter - canvasCenter|=100, MaxExpansionFactor=0.40
        //   → 拡張オフセット = ±40
        // overlap nudge (確率発火): chaos=1 で 50% 確率、最大 ±40 (= 200×0.20)
        // polish pass (高 chaos で破綻防止): 孤立 + 過密ガード で各 ±24 (= 200×0.12)
        //   合計 OffsetX/Y 最悪ケース = ±62 + ±40 + ±40 + ±24 + ±24 = ±190
        // 回転 ≤ ±8 度 (triangular) + 整列破壊 ±1.5 = ±9.5
        // フレーム = 12 / 36
        // シャドウ alpha = 64
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 1.0, seed: 42);

        foreach (var item in result)
        {
            item.OffsetX.Should().BeInRange(-190.0, 190.0);
            item.OffsetY.Should().BeInRange(-190.0, 190.0);
            item.RotationDeg.Should().BeInRange(-9.5, 9.5);
            item.RotationPivotOffsetX.Should().BeInRange(-36.0, 36.0);  // 200×0.18
            item.RotationPivotOffsetY.Should().BeInRange(-36.0, 36.0);
            item.FrameSidePx.Should().Be(12);
            item.FrameBottomPx.Should().Be(36);
            item.FrameAlpha.Should().Be(255);
            item.ShadowAlpha.Should().BeInRange((byte)51, (byte)77);  // 64 ± 20%
            item.ShadowOffsetX.Should().BeInRange(0.0, 4.0);  // base 2 ± 1
            item.ShadowOffsetY.Should().BeInRange(2.0, 6.0);  // base 4 ± 1
            item.ShadowSigma.Should().BeInRange(0.0, 6.0);    // base 4 ± 1 + clamp 余裕
        }
    }

    [Fact]
    public void Compute_Chaos_At_OverlapRampStart_Has_No_Nudge()
    {
        // chaos=0.6 ちょうどでは overlap nudge は発火しない (> 0.6 が条件)。
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 0.6, seed: 42);

        foreach (var item in result)
        {
            // disorderRamp(0.6) = (0.6-0.20)/0.80 = 0.5
            // 位置オフセット最大 = 0.5 × 200 × (0.05 + 2×0.10 + 0.06) = 31
            // 拡張: expansionFactor = 1.0 + 0.4×0.5 = 1.20 → corner 100×0.20 = 20
            // per-item expansion mul は chaos > OverlapChaosThreshold(0.6) で発火、ここでは 0.6 ジャストなので
            // 一律 1.0 (発火しない条件 clamped > 0.6)
            // 合計絶対値 ≤ 31 + 20 = 51
            item.OffsetX.Should().BeInRange(-55.0, 55.0);
            item.OffsetY.Should().BeInRange(-55.0, 55.0);
        }
    }

    [Fact]
    public void Compute_Overlap_Probability_Ramps_Smoothly_With_Chaos()
    {
        // 重なり発火率を chaos に対して連続的に増やす UX 契約を担保。
        // 100 シードで「nudge と思われる極端オフセット (片軸 > 60px)」の発生数を
        // 各 chaos 値で測定し、chaos が大きいほど発生数が単調に増えることを確認。
        // (閾値 binary 切替だと chaos=0.69→0.71 で急増する。連続化により滑らかなランプになる)
        const int trials = 100;
        int CountLargeOffset(double chaos)
        {
            int count = 0;
            for (int seed = 1; seed <= trials; seed++)
            {
                var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: chaos, seed: seed);
                foreach (var item in result)
                {
                    if (Math.Abs(item.OffsetX) > 60 || Math.Abs(item.OffsetY) > 60) count++;
                }
            }
            return count;
        }
        var c060 = CountLargeOffset(0.60);  // 0% 確率: 偶然のみ
        var c075 = CountLargeOffset(0.75);  // 18.75% 確率
        var c090 = CountLargeOffset(0.90);  // 37.5% 確率
        var c100 = CountLargeOffset(1.00);  // 50% 確率

        c060.Should().BeLessThan(c075,
            "chaos=0.60 (nudge 0%) より chaos=0.75 (nudge 18.75%) で大きいオフセット件数が増える");
        c075.Should().BeLessThan(c100,
            "chaos=0.75 より chaos=1.00 でさらに増える (連続変化)");
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

        // 平均が拡張方向 (left/up=負、right/down=正) に偏る。
        // 注意: chaos > OverlapChaosThreshold (=0.6) では per-item で拡張倍率が
        // [-0.4, +1.0] の範囲でばらつく (重なり生成のため)。期待平均拡張倍率は
        // 0.3 倍 → 平均拡張オフセットは -12 程度。閾値は -8 に設定 (符号が拡張方向
        // である事を担保しつつ、ばらつきの影響でブレるので余裕を持たせる)。
        (sumOffsetX0 / trials).Should().BeLessThan(-8.0,
            "左側 placement は拡張で左方向 (-X) に平均的にシフトするはず");
        (sumOffsetX1 / trials).Should().BeGreaterThan(8.0,
            "右側 placement は拡張で右方向 (+X) に平均的にシフトするはず");
        (sumOffsetY0 / trials).Should().BeLessThan(-8.0,
            "上側 placement は拡張で上方向 (-Y) に平均的にシフトするはず");
        (sumOffsetY3 / trials).Should().BeGreaterThan(8.0,
            "下側 placement は拡張で下方向 (+Y) に平均的にシフトするはず");
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
    public void Compute_PolishPass_Does_Not_Run_At_Low_Chaos()
    {
        // disorderRamp <= 0.5 (= chaos <= 0.6) では polish pass は無効化される。
        // chaos=0.5 で polish の影響がないことを「polish 有効ライン直上 (0.61) との差分」
        // ではなく「決定論的な生 compute 結果と一致するか」で代替検証 (polish 内部分岐
        // の偽性を担保)。chaos=0.5 で disorderRamp = (0.5-0.20)/0.80 = 0.375。
        var result = PhotoBoardLayout.Compute(SampleGrid2x2(), SampleCanvas, chaos: 0.5, seed: 99);

        // polish が走らないことを直接観測する手段はないが、bounds 内で安定動作することを
        // 確認 (低 chaos で polish が走ると nudge が突然加算されて不連続になる)
        foreach (var item in result)
        {
            // chaos=0.5: disorderRamp=0.375
            //   位置オフセット最大 = 0.375 × 200 × (0.05 + 2×0.10 + 0.06) = 23.25
            //   拡張: factor=1+0.4×0.375=1.15 → corner ±15
            //   overlap nudge は chaos>0.6 のみなので非発火
            //   合計 ≤ ±39
            item.OffsetX.Should().BeInRange(-40.0, 40.0);
            item.OffsetY.Should().BeInRange(-40.0, 40.0);
        }
    }

    [Fact]
    public void Compute_PolishPass_Preserves_Determinism()
    {
        // Polish pass は内部で nearest-neighbor / クラスタ判定 / 整列検出をするが、
        // RNG は 1 つしか使わないので同じシードで同じ結果になる。
        var input = SampleGrid2x2();
        var a = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 0.95, seed: 12345);
        var b = PhotoBoardLayout.Compute(input, SampleCanvas, chaos: 0.95, seed: 12345);

        a.Should().BeEquivalentTo(b);
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
            // フレーム / シャドウは飽和 (alpha は ±20% per-item で揺らぐ)
            item.FrameSidePx.Should().Be(12);
            item.FrameBottomPx.Should().Be(36);
            item.FrameAlpha.Should().Be(255);
            item.ShadowAlpha.Should().BeInRange((byte)51, (byte)77);
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
