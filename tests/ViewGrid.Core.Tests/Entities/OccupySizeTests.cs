using System;
using FluentAssertions;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Core.Tests.Entities;

public sealed class OccupySizeTests
{
    [Fact]
    public void OneByOne_Has_Unit_Dimensions()
    {
        OccupySize.OneByOne.Width.Should().Be(1);
        OccupySize.OneByOne.Height.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void NonPositive_Dimensions_Are_Rejected(int width, int height)
    {
        var act = () => _ = new OccupySize(width, height);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(10, 10)]
    public void Positive_Dimensions_Are_Accepted(int width, int height)
    {
        var size = new OccupySize(width, height);
        size.Width.Should().Be(width);
        size.Height.Should().Be(height);
    }
}
