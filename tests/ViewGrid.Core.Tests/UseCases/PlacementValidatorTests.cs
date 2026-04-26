using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;
using Xunit;

namespace ViewGrid.Core.Tests.UseCases;

public sealed class PlacementValidatorTests
{
    [Theory]
    [InlineData(0, 0, 1, 1, 3, 3)]
    [InlineData(2, 2, 1, 1, 3, 3)]
    [InlineData(0, 0, 3, 3, 3, 3)]
    [InlineData(1, 1, 2, 2, 3, 3)]
    public void Returns_Valid_For_In_Bounds_Single_Or_Multi_Cell_Placement(
        int x, int y, int w, int h, int rows, int cols)
    {
        var result = PlacementValidator.Validate(
            new OccupySize(w, h),
            new CellPosition(x, y),
            rows,
            cols,
            []);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(2, 2, 2, 1, 3, 3)] // 横方向で 1 列はみ出す
    [InlineData(2, 2, 1, 2, 3, 3)] // 縦方向で 1 行はみ出す
    [InlineData(3, 0, 1, 1, 3, 3)] // x がグリッド外
    public void Returns_OutOfBounds_When_Placement_Exceeds_Grid(
        int x, int y, int w, int h, int rows, int cols)
    {
        var result = PlacementValidator.Validate(
            new OccupySize(w, h),
            new CellPosition(x, y),
            rows,
            cols,
            []);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(PlacementInvalidReason.OutOfBounds);
    }

    [Fact]
    public void Returns_Conflict_When_Overlapping_Existing_Single_Cell()
    {
        var existingId = Guid.NewGuid();
        var existing = new ExistingPlacement[]
        {
            new(existingId, new CellPosition(1, 1), OccupySize.OneByOne),
        };

        var result = PlacementValidator.Validate(
            OccupySize.OneByOne,
            new CellPosition(1, 1),
            3, 3,
            existing);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(PlacementInvalidReason.Conflict);
        result.ConflictingPlacementId.Should().Be(existingId);
    }

    [Fact]
    public void Returns_Conflict_When_Overlapping_Existing_Multi_Cell()
    {
        var existingId = Guid.NewGuid();
        var existing = new ExistingPlacement[]
        {
            new(existingId, new CellPosition(0, 0), new OccupySize(2, 2)),
        };

        // 配置先 (1,1) は既存 2x2 の右下端と重なる
        var result = PlacementValidator.Validate(
            OccupySize.OneByOne,
            new CellPosition(1, 1),
            3, 3,
            existing);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(PlacementInvalidReason.Conflict);
    }

    [Fact]
    public void Returns_Valid_When_Adjacent_But_Not_Overlapping()
    {
        var existing = new ExistingPlacement[]
        {
            new(Guid.NewGuid(), new CellPosition(0, 0), new OccupySize(1, 1)),
        };

        var result = PlacementValidator.Validate(
            OccupySize.OneByOne,
            new CellPosition(1, 0),
            3, 3,
            existing);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Excluded_Placement_Is_Skipped_For_Self_Move_Validation()
    {
        var movingId = Guid.NewGuid();
        var existing = new ExistingPlacement[]
        {
            new(movingId, new CellPosition(0, 0), OccupySize.OneByOne),
        };

        // 自分自身を除外して同位置を検証 → 重複扱いにならない
        var result = PlacementValidator.Validate(
            OccupySize.OneByOne,
            new CellPosition(0, 0),
            3, 3,
            existing,
            excludePlacementId: movingId);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void OccupiedCells_Enumerates_Row_Major()
    {
        var cells = PlacementValidator.OccupiedCells(
            new CellPosition(2, 1), new OccupySize(2, 2)).ToList();

        cells.Should().BeEquivalentTo(new[]
        {
            new CellPosition(2, 1),
            new CellPosition(3, 1),
            new CellPosition(2, 2),
            new CellPosition(3, 2),
        });
    }
}
