using System;
using FluentAssertions;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Core.Tests.Entities;

public sealed class CellPositionTests
{
    [Fact]
    public void Origin_Is_Valid()
    {
        var pos = new CellPosition(0, 0);
        pos.X.Should().Be(0);
        pos.Y.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Negative_Coordinates_Are_Rejected(int x, int y)
    {
        var act = () => _ = new CellPosition(x, y);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
