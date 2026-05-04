using FluentAssertions;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Tests.Entities;

/// <summary>
/// <see cref="ManualCropFraction"/> の値域・換算・センチネル判定のテスト。
/// 設計は <see cref="AutoCropFraction"/> を踏襲しており、両者の挙動は揃っているべき。
/// </summary>
public sealed class ManualCropFractionTests
{
    [Fact]
    public void Full_Is_Whole_Image()
    {
        ManualCropFraction.Full.Should().Be(new ManualCropFraction(0.0, 0.0, 1.0, 1.0));
        ManualCropFraction.Full.IsFull().Should().BeTrue();
    }

    [Fact]
    public void IsFull_True_Within_Tolerance()
    {
        // 1e-9 のずれは許容範囲内 → IsFull
        var f = new ManualCropFraction(1e-9, 1e-9, 1.0 - 1e-9, 1.0 - 1e-9);
        f.IsFull().Should().BeTrue();
    }

    [Fact]
    public void IsFull_False_Outside_Tolerance()
    {
        // 0.01 のずれは許容範囲外 → IsFull ではない
        var f = new ManualCropFraction(0.01, 0.0, 0.99, 1.0);
        f.IsFull().Should().BeFalse();
    }

    [Fact]
    public void ToPixelBbox_Half_Half_From_Center()
    {
        var f = new ManualCropFraction(0.25, 0.25, 0.5, 0.5);

        var (x, y, w, h) = f.ToPixelBbox(800, 600);

        x.Should().Be(200);
        y.Should().Be(150);
        w.Should().Be(400);
        h.Should().Be(300);
    }

    [Fact]
    public void ToPixelBbox_Clamps_When_Exceeding_Bounds()
    {
        // 不正な値（X+W > 1）が来ても、Clamp で width 内に収める
        var f = new ManualCropFraction(0.5, 0.5, 1.0, 1.0);

        var (x, y, w, h) = f.ToPixelBbox(100, 100);

        x.Should().Be(50);
        y.Should().Be(50);
        w.Should().Be(50);  // (100 - x) でクランプ
        h.Should().Be(50);
    }

    [Fact]
    public void ToPixelBbox_Full_Returns_Whole_Image()
    {
        var (x, y, w, h) = ManualCropFraction.Full.ToPixelBbox(640, 480);
        x.Should().Be(0);
        y.Should().Be(0);
        w.Should().Be(640);
        h.Should().Be(480);
    }
}
