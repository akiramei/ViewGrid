using FluentAssertions;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Tests.Entities;

/// <summary>
/// 実効クロップ優先規則 (R-08) の唯一源 <see cref="CropFraction.ResolveEffective"/> の Core oracle。
/// IO-1 是正: ImageCropResolver / CopyPropertiesViewModel.EffectiveCropPreview /
/// SkiaGridImageRenderer の 3 (4) 実装が本関数に優先判定を委譲したため、優先規則の唯一の決定的 oracle を
/// Application 層 (ImageCropResolverTests) から Core へ昇格させ、短絡・排他・full→null を固定する。
/// </summary>
public sealed class CropFractionResolveEffectiveTests
{
    [Fact]
    public void Manual_NonFull_Wins_And_Maps_Verbatim()
    {
        var manual = new ManualCropFraction(0.1, 0.2, 0.3, 0.4);
        CropFraction.ResolveEffective(manual, auto: null)
            .Should().Be(new CropFraction(0.1, 0.2, 0.3, 0.4));
    }

    [Fact]
    public void Manual_Full_Returns_Null()
    {
        // ManualCrop が full (クロップ無効) なら null。
        CropFraction.ResolveEffective(ManualCropFraction.Full, auto: null)
            .Should().BeNull();
    }

    [Fact]
    public void Manual_Exclusive_Short_Circuits_Auto()
    {
        // ★ 排他・短絡: ManualCrop があれば AutoCrop は一切参照しない。
        // Manual=full でも Auto に *落ちず* null（旧 site 4 ComputeRenderedGeometryRect の
        // 「Manual=full のとき Auto へ fall through」する latent drift を、唯一源で正す）。
        var auto = new AutoCropFraction(0.05, 0.05, 0.9, 0.9); // non-full
        CropFraction.ResolveEffective(ManualCropFraction.Full, auto).Should().BeNull();

        // Manual=non-full + Auto=non-full → Manual が勝つ (Auto は無視)。
        var manual = new ManualCropFraction(0.1, 0.1, 0.5, 0.5);
        CropFraction.ResolveEffective(manual, auto)
            .Should().Be(new CropFraction(0.1, 0.1, 0.5, 0.5));
    }

    [Fact]
    public void Auto_Used_When_Manual_Null()
    {
        var auto = new AutoCropFraction(0.05, 0.05, 0.9, 0.9);
        CropFraction.ResolveEffective(manual: null, auto)
            .Should().Be(new CropFraction(0.05, 0.05, 0.9, 0.9));
    }

    [Fact]
    public void Auto_Full_Returns_Null()
    {
        // AutoCrop が full なら null (full→null を決定点で明示。上流の null 化に依存しない)。
        CropFraction.ResolveEffective(manual: null, AutoCropFraction.Full)
            .Should().BeNull();
    }

    [Fact]
    public void Both_Null_Returns_Null()
    {
        CropFraction.ResolveEffective(manual: null, auto: null).Should().BeNull();
    }
}
