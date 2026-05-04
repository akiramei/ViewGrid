using FluentAssertions;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Tests.Entities;

/// <summary>
/// <see cref="PhotoBoardStyleCoefficients.For"/> ファクトリの境界値テスト。
/// スタイル × 強度の合成が期待通りに動くことを担保する。
/// </summary>
public sealed class PhotoBoardStyleCoefficientsTests
{
    [Fact]
    public void For_Intensity_Half_Returns_Style_Baseline()
    {
        // intensity=0.5 → factor=1.0、 baseline 値そのまま (Frame/Shadow/Anchor/Polish 含む)。
        var natural = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 0.5);
        var baseline = PhotoBoardStyleCoefficients.Base(PhotoBoardStyle.Natural);

        natural.Should().BeEquivalentTo(baseline);
    }

    [Fact]
    public void For_Intensity_Zero_Reduces_Variability_Coefs_To_Zero()
    {
        // intensity=0 → factor=0、 Rotation/Jitter/Overlap/Drift がすべて 0。
        // Frame/Shadow/Anchor/Polish は影響を受けず baseline のまま、 Expansion は 1.0。
        foreach (var style in new[] { PhotoBoardStyle.Natural, PhotoBoardStyle.Rough, PhotoBoardStyle.Scattered })
        {
            var coefs = PhotoBoardStyleCoefficients.For(style, 0.0);
            coefs.RotationStrength.Should().Be(0.0);
            coefs.JitterStrength.Should().Be(0.0);
            coefs.OverlapProbability.Should().Be(0.0);
            coefs.DriftStrength.Should().Be(0.0);
            coefs.Expansion.Should().Be(1.0);

            var baseline = PhotoBoardStyleCoefficients.Base(style);
            coefs.FrameStrength.Should().Be(baseline.FrameStrength);
            coefs.ShadowStrength.Should().Be(baseline.ShadowStrength);
            coefs.AnchorDecay.Should().Be(baseline.AnchorDecay);
            coefs.PolishEnabled.Should().Be(baseline.PolishEnabled);
        }
    }

    [Fact]
    public void For_Intensity_One_Doubles_Variability_Coefs()
    {
        // intensity=1.0 → factor=2.0、 baseline × 2 (clamp [0,1] あり)。
        // Scattered baseline: rotation=0.9, jitter=0.9, overlap=0.4, drift=0.7, expansion=1.35
        var coefs = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0);

        coefs.RotationStrength.Should().Be(1.0);     // min(0.9 × 2, 1.0)
        coefs.JitterStrength.Should().Be(1.0);       // min(0.9 × 2, 1.0)
        coefs.OverlapProbability.Should().Be(0.8);   // 0.4 × 2 = 0.8
        coefs.DriftStrength.Should().Be(1.0);        // min(0.7 × 2, 1.0)
        coefs.Expansion.Should().BeApproximately(1.70, 1e-9);  // 1.0 + (1.35 - 1.0) × 2

        // Frame/Shadow/Anchor/Polish はスケール非影響
        coefs.FrameStrength.Should().Be(1.0);
        coefs.ShadowStrength.Should().Be(1.0);
        coefs.AnchorDecay.Should().Be(0.30);
        coefs.PolishEnabled.Should().BeTrue();
    }

    [Fact]
    public void For_Intensity_Out_Of_Range_Is_Clamped()
    {
        // intensity = -1 → 0 にクランプ、 intensity = 5 → 1 にクランプ
        var lowExtreme = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, -1.0);
        var lowZero = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 0.0);
        lowExtreme.Should().BeEquivalentTo(lowZero);

        var highExtreme = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 5.0);
        var highOne = PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Natural, 1.0);
        highExtreme.Should().BeEquivalentTo(highOne);
    }

    [Fact]
    public void Base_Returns_Different_Values_For_Each_Style()
    {
        // 3 スタイルが意味的に異なる baseline を持つことを確認 (個性の担保)。
        var natural = PhotoBoardStyleCoefficients.Base(PhotoBoardStyle.Natural);
        var rough = PhotoBoardStyleCoefficients.Base(PhotoBoardStyle.Rough);
        var scattered = PhotoBoardStyleCoefficients.Base(PhotoBoardStyle.Scattered);

        // ナチュラル < ラフ < バラ撒き の順で disorder 系係数が増える
        natural.RotationStrength.Should().BeLessThan(rough.RotationStrength);
        rough.RotationStrength.Should().BeLessThan(scattered.RotationStrength);

        natural.JitterStrength.Should().BeLessThan(scattered.JitterStrength);
        natural.OverlapProbability.Should().BeLessThan(scattered.OverlapProbability);
        natural.Expansion.Should().BeLessThan(scattered.Expansion);

        // フレーム / シャドウ / アンカー / polish は同等 (写真ボード感はスタイル間で共通)
        natural.FrameStrength.Should().Be(rough.FrameStrength).And.Be(scattered.FrameStrength);
        natural.ShadowStrength.Should().Be(rough.ShadowStrength).And.Be(scattered.ShadowStrength);
        natural.AnchorDecay.Should().Be(rough.AnchorDecay).And.Be(scattered.AnchorDecay);
        natural.PolishEnabled.Should().Be(rough.PolishEnabled).And.Be(scattered.PolishEnabled);
    }
}
