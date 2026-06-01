using FluentAssertions;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Tests.Entities;

/// <summary>
/// CropFraction (実効クロップ比率の統合型) の VO メソッド契約を固定する。
/// Pattern 1 micro-pilot (F-P10) の oracle 補強: ImageCropResolverTests は precedence を網羅するが
/// CropFraction の IsFull / ToPixelBbox / From は専用テストが無かった。
/// 丸めの中間値 (x.5) に依存しない値を選び、丸めモードに頑健なテストにしている
/// (丸めモードの仕様未指定は F-P10 の d-3 フィードバックとして別途記録)。
/// </summary>
public sealed class CropFractionTests
{
    [Fact]
    public void Full_Is_Whole_Region()
    {
        CropFraction.Full.Should().Be(new CropFraction(0.0, 0.0, 1.0, 1.0));
        CropFraction.Full.IsFull().Should().BeTrue();
    }

    [Fact]
    public void IsFull_True_Within_Tolerance()
    {
        new CropFraction(0.0, 0.0, 1.0, 1.0).IsFull().Should().BeTrue();
        // 既定 tolerance 1e-6 より内側のゆらぎは full 扱い。
        new CropFraction(1e-7, 0.0, 1.0, 1.0 - 1e-7).IsFull().Should().BeTrue();
    }

    [Fact]
    public void IsFull_False_When_Cropped()
    {
        new CropFraction(0.1, 0.0, 1.0, 1.0).IsFull().Should().BeFalse();
        new CropFraction(0.0, 0.0, 0.9, 1.0).IsFull().Should().BeFalse();
    }

    [Fact]
    public void ToPixelBbox_Maps_Fraction_To_Pixels()
    {
        // 中間値を避けた値: 0.05*800=40, 0.05*600=30, 0.9*800=720, 0.9*600=540 (いずれも整数)。
        new CropFraction(0.05, 0.05, 0.9, 0.9)
            .ToPixelBbox(800, 600)
            .Should().Be((40, 30, 720, 540));
    }

    [Fact]
    public void ToPixelBbox_Clamps_Width_To_Remaining()
    {
        // x=50 のとき w は残り (100-50=50) に丸められ、画像外へはみ出さない (0.9*100=90 → 50)。
        new CropFraction(0.5, 0.0, 0.9, 1.0)
            .ToPixelBbox(100, 100)
            .Should().Be((50, 0, 50, 100));
    }

    [Fact]
    public void ToPixelBbox_Full_Covers_Whole_Image()
    {
        CropFraction.Full.ToPixelBbox(640, 480).Should().Be((0, 0, 640, 480));
    }

    [Fact]
    public void ToPixelBbox_Midpoint_Rounds_ToEven_AsBuilt()
    {
        // ★ F-P12 oracle 硬化: midpoint (x.5) の丸めモードを決定的に固定する falsifier。
        // 0.5 × 5 = 2.5 (ちょうど中間値)。実装は System.Math.Round 既定 = ToEven (銀行家丸め) で X=2。
        // MidpointRounding.AwayFromZero なら X=3 となり、F-P10 の blind 生成物 (AwayFromZero) はここで FAIL する。
        // 注意: これは「現実装が ToEven である」という as-built 事実の再現を固定するもので、
        //       ToEven が *意図された* 丸め方針か (AwayFromZero との優劣) は未決定 (d-3 の決定点 D2a)。
        new CropFraction(0.5, 0.0, 1.0, 1.0).ToPixelBbox(5, 5).X.Should().Be(2);
        // 別の中間値でも ToEven を確認: 0.5 × 1 = 0.5 → 0 (ToEven) / 1 (AwayFromZero)。
        new CropFraction(0.5, 0.0, 1.0, 1.0).ToPixelBbox(1, 1).X.Should().Be(0);
    }

    [Fact]
    public void From_AutoCropFraction_Maps_Fields()
    {
        CropFraction.From(new AutoCropFraction(0.1, 0.2, 0.3, 0.4))
            .Should().Be(new CropFraction(0.1, 0.2, 0.3, 0.4));
    }

    [Fact]
    public void From_ManualCropFraction_Maps_Fields()
    {
        CropFraction.From(new ManualCropFraction(0.15, 0.25, 0.35, 0.45))
            .Should().Be(new CropFraction(0.15, 0.25, 0.35, 0.45));
    }
}
