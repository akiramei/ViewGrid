using FluentAssertions;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Tests.Entities;

/// <summary>
/// <see cref="RegionRectFraction"/> の値域・換算・有効性判定のテスト。
/// 形は <see cref="ManualCropFraction"/> と同型だが、 意味が異なるため別型として
/// 切ってある (<see cref="RegionRectFraction"/> の docstring 参照)。
/// </summary>
public sealed class RegionRectFractionTests
{
    [Fact]
    public void ToPixelBbox_Half_Half_From_Center()
    {
        var f = new RegionRectFraction(0.25, 0.25, 0.5, 0.5);

        var (x, y, w, h) = f.ToPixelBbox(800, 600);

        x.Should().Be(200);
        y.Should().Be(150);
        w.Should().Be(400);
        h.Should().Be(300);
    }

    [Fact]
    public void ToPixelBbox_Clamps_When_Exceeding_Bounds()
    {
        // 浮動小数誤差で X+W が 1.0 を僅かに超える状況でも、 ピクセル幅の合計は
        // 画像幅を超えないように clamp される。
        var f = new RegionRectFraction(0.6, 0.0, 0.5, 1.0);

        var (x, y, w, h) = f.ToPixelBbox(100, 100);

        x.Should().Be(60);
        y.Should().Be(0);
        w.Should().Be(40); // 画像幅 100 - x(60) = 40 にクランプ
        h.Should().Be(100);
    }

    [Fact]
    public void IsValid_True_For_Reasonable_Rect()
    {
        new RegionRectFraction(0.1, 0.2, 0.3, 0.4).IsValid().Should().BeTrue();
    }

    [Fact]
    public void IsValid_False_For_Zero_Width()
    {
        new RegionRectFraction(0.1, 0.1, 0.0, 0.5).IsValid().Should().BeFalse();
    }

    [Fact]
    public void IsValid_False_For_Negative_Origin()
    {
        new RegionRectFraction(-0.01, 0.0, 0.5, 0.5).IsValid().Should().BeFalse();
    }

    [Fact]
    public void IsValid_False_When_Exceeds_Right_Edge()
    {
        // X(0.6) + W(0.5) = 1.1 > 1.0 (許容誤差超え) → 無効
        new RegionRectFraction(0.6, 0.0, 0.5, 0.5).IsValid().Should().BeFalse();
    }

    [Fact]
    public void IsValid_True_Within_Tolerance_At_Right_Edge()
    {
        // X+W が 1.0 + 1e-12 程度なら許容誤差内で valid
        new RegionRectFraction(0.5, 0.0, 0.5 + 1e-12, 1.0).IsValid().Should().BeTrue();
    }

    [Fact]
    public void Record_Equality_Works_For_Same_Values()
    {
        var a = new RegionRectFraction(0.1, 0.2, 0.3, 0.4);
        var b = new RegionRectFraction(0.1, 0.2, 0.3, 0.4);
        a.Should().Be(b);
    }
}
