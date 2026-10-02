using FluentAssertions;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Tests.UseCases;

/// <summary>
/// <see cref="PlacementGeometry.ResolveWeightedIndex"/> の契約。 重み付き (Star) 割り付けの列・行で、
/// ドラッグ先のセル判定が均等割りとずれていた不具合 (R03) の回帰防止。
/// </summary>
public sealed class PlacementGeometryWeightedIndexTests
{
    private static readonly int[] Weights311 = [3, 1, 1];
    private static readonly int[] Weights12 = [1, 2];
    private static readonly int[] WeightsWithZero = [0, 1];
    private static readonly int[] WrongLength = [1, 2, 3];

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(359.9, 0)]
    [InlineData(360.0, 1)] // 境界 (列 0 は 0..360)
    [InlineData(479.9, 1)]
    [InlineData(480.0, 2)]
    [InlineData(599.9, 2)]
    public void Width600_Weights_3_1_1_Uses_Boundaries_0_360_480_600(double x, int expected)
    {
        PlacementGeometry.ResolveWeightedIndex(Weights311, 3, x, 600).Should().Be(expected);
    }

    [Fact]
    public void Reported_Case_x250_Is_Still_Column_0_Not_Column_1()
    {
        // 報告の再現: 幅 600、 重み [3,1,1]。 x=250 は第 1 列の内側 (均等割りだと 200..400 で第 2 列になっていた)。
        PlacementGeometry.ResolveWeightedIndex(Weights311, 3, 250, 600).Should().Be(0);
        // 均等割りの境界 (x=200) は第 1 列の内側のまま。
        PlacementGeometry.ResolveWeightedIndex(Weights311, 3, 200, 600).Should().Be(0);
    }

    [Fact]
    public void Missing_Or_Mismatched_Weights_Fall_Back_To_Even_Split()
    {
        PlacementGeometry.ResolveWeightedIndex(null, 3, 250, 600).Should().Be(1);
        PlacementGeometry.ResolveWeightedIndex(WrongLength, 2, 250, 600).Should().Be(0);
        PlacementGeometry.ResolveWeightedIndex(WrongLength, 2, 350, 600).Should().Be(1);
    }

    [Fact]
    public void Weights_Below_One_Are_Treated_As_One_Like_The_Display()
    {
        // 表示側は Math.Max(1, weight) で割り付けるので、 [0,1] は [1,1] と同じ。
        PlacementGeometry.ResolveWeightedIndex(WeightsWithZero, 2, 49, 100).Should().Be(0);
        PlacementGeometry.ResolveWeightedIndex(WeightsWithZero, 2, 51, 100).Should().Be(1);
    }

    [Fact]
    public void Out_Of_Range_Positions_Clamp_To_The_Ends()
    {
        PlacementGeometry.ResolveWeightedIndex(Weights12, 2, -10, 90).Should().Be(0);
        PlacementGeometry.ResolveWeightedIndex(Weights12, 2, 90, 90).Should().Be(1);
        PlacementGeometry.ResolveWeightedIndex(Weights12, 2, 1000, 90).Should().Be(1);
    }

    [Fact]
    public void Weighted_Rows_Work_The_Same_Way()
    {
        // 行重み [1,2] (高さ 90): 境界は 30。
        PlacementGeometry.ResolveWeightedIndex(Weights12, 2, 29, 90).Should().Be(0);
        PlacementGeometry.ResolveWeightedIndex(Weights12, 2, 31, 90).Should().Be(1);
    }

    [Theory]
    [InlineData(1, 50.0, 100.0)]
    [InlineData(0, 50.0, 100.0)]
    [InlineData(3, 50.0, 0.0)]
    public void Single_Cell_Or_Empty_Total_Returns_Zero(int count, double position, double total)
    {
        PlacementGeometry.ResolveWeightedIndex(Weights311, count, position, total).Should().Be(0);
    }
}
