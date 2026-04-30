using System;
using FluentAssertions;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Core.Tests.Entities;

public sealed class ImageCopyTests
{
    [Fact]
    public void Characteristics_Aggregates_Individual_Value_Objects()
    {
        var now = DateTimeOffset.UtcNow;
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContainShrinkOnly,
            Alignment = new Alignment(AnchorX.Center, AnchorY.Top),
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };

        copy.Characteristics.ScalingMode.Should().Be(ScalingMode.UniformContainShrinkOnly);
        copy.Characteristics.Alignment.Should().Be(new Alignment(AnchorX.Center, AnchorY.Top));
    }
}
