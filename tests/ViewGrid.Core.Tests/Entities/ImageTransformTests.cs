using FluentAssertions;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Core.Tests.Entities;

public sealed class ImageTransformTests
{
    [Fact]
    public void Identity_Has_No_Rotation_Or_Flip()
    {
        ImageTransform.Identity.Rotation.Should().Be(Rotation.None);
        ImageTransform.Identity.FlipX.Should().BeFalse();
        ImageTransform.Identity.FlipY.Should().BeFalse();
    }

    [Theory]
    [InlineData(Rotation.None, 0)]
    [InlineData(Rotation.Cw90, 90)]
    [InlineData(Rotation.Cw180, 180)]
    [InlineData(Rotation.Cw270, 270)]
    public void Rotation_Enum_Matches_Degree_Values(Rotation rotation, int expectedDegrees)
    {
        ((int)rotation).Should().Be(expectedDegrees);
    }

    [Fact]
    public void Records_With_Same_Values_Are_Equal()
    {
        var a = new ImageTransform(Rotation.Cw90, true, false);
        var b = new ImageTransform(Rotation.Cw90, true, false);
        a.Should().Be(b);
    }
}
