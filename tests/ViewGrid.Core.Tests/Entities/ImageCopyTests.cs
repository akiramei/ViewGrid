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
            TrimmingAnchor = TrimmingAnchor.TopCenter,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };

        copy.Characteristics.ScalingMode.Should().Be(ScalingMode.UniformContainShrinkOnly);
        copy.Characteristics.TrimmingAnchor.Should().Be(TrimmingAnchor.TopCenter);
        copy.Characteristics.Alignment.Should().Be(Alignment.Center);
    }
}
