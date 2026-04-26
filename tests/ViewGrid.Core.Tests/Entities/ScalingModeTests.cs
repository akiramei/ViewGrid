using FluentAssertions;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Core.Tests.Entities;

public sealed class ScalingModeTests
{
    [Fact]
    public void Enum_Has_Six_Members_In_Documented_Order()
    {
        // 順序が変わると DB の int 値が破壊される。enum の順序固定を回帰検出するためのガード。
        ((int)ScalingMode.None).Should().Be(0);
        ((int)ScalingMode.UniformContain).Should().Be(1);
        ((int)ScalingMode.UniformContainShrinkOnly).Should().Be(2);
        ((int)ScalingMode.UniformContainEnlargeOnly).Should().Be(3);
        ((int)ScalingMode.UniformCover).Should().Be(4);
        ((int)ScalingMode.Fill).Should().Be(5);
    }
}
