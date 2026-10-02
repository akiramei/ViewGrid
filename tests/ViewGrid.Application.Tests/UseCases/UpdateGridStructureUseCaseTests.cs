using System.Collections.Immutable;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdateGridStructureUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdateGridStructureUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdateGridStructureUseCase(_fx.GridRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    private async Task<GridCanvas> SeedGridAsync(
        int rows, int cols, ImmutableArray<int>? colWeights = null, ImmutableArray<bool>? colLocked = null)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(), Name = "g", GridRows = rows, GridCols = cols,
            ColWeights = colWeights ?? GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            ColLocked = colLocked ?? [],
            CanvasSize = new PixelSize(400, 400),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        (await _fx.GridRepository.AddAsync(grid)).IsError.Should().BeFalse();
        return grid;
    }

    private async Task<GridPlacement> PlaceAsync(GridCanvas grid, int x, int y, OccupySize? size = null)
    {
        var asset = await _fx.SeedAssetAsync($"h{x}{y}{Guid.NewGuid():N}".PadRight(64, '0'));
        var copy = await _fx.SeedCopyAsync(asset.Id, "c");
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placement = (await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(x, y))).Value;
        if (size is { } s)
            (await new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository)
                .ExecuteAsync(placement.Id, s)).IsError.Should().BeFalse();
        return placement;
    }

    [Fact]
    public void WithRowAdded_Appends_The_Average_Weight_And_An_Unlocked_Row()
    {
        var s = new GridStructure(
            2, 3, [3, 1, 1], [2, 4], [true, false, false], [false, true]);

        var added = s.WithRowAdded();

        added.Rows.Should().Be(3);
        added.RowWeights.Should().Equal(2, 4, 3); // 新しい行の比率は既存の平均
        added.RowLocked.Should().Equal(false, true, false);
        added.Cols.Should().Be(3);
        added.ColWeights.Should().Equal(3, 1, 1); // 列側は変わらない
    }

    [Fact]
    public void WithColumnAdded_Averages_Rounds_And_Never_Goes_Below_One()
    {
        var s = new GridStructure(1, 3, [3, 1, 1], [1], [false, false, false], [false]);

        var added = s.WithColumnAdded();

        added.Cols.Should().Be(4);
        added.ColWeights.Should().Equal(3, 1, 1, 2); // 平均 1.67 → 2
        added.ColLocked.Should().Equal(false, false, false, false);
    }

    [Fact]
    public async Task Adding_A_Row_Keeps_Every_Placement_And_Its_Offset()
    {
        var grid = await SeedGridAsync(2, 2);
        var p = await PlaceAsync(grid, 1, 1);
        await _fx.PlacementRepository.UpdateAsync(new GridPlacement
        {
            Id = p.Id, GridId = p.GridId, CopyId = p.CopyId, Position = p.Position, OccupySize = p.OccupySize,
            PlacementOrder = p.PlacementOrder, CreatedAt = p.CreatedAt, PixelOffsetX = 12, PixelOffsetY = -7,
        });
        var before = GridStructure.From((await _fx.GridRepository.FindByIdAsync(grid.Id))!);

        var result = await _useCase.ExecuteAsync(grid.Id, before.WithRowAdded());

        result.IsError.Should().BeFalse();
        var stored = (await _fx.GridRepository.FindByIdAsync(grid.Id))!;
        stored.GridRows.Should().Be(3);
        stored.GridCols.Should().Be(2);
        stored.RowWeights.Should().HaveCount(3);
        stored.Name.Should().Be("g");
        stored.CanvasSize.Should().Be(new PixelSize(400, 400));
        var placement = (await _fx.PlacementRepository.FindByIdAsync(p.Id))!;
        placement.Position.Should().Be(new CellPosition(1, 1));
        placement.PixelOffsetX.Should().Be(12, "配置固有の微調整はそのまま");
        placement.PixelOffsetY.Should().Be(-7);
    }

    [Fact]
    public async Task Existing_Weights_And_Locks_Survive_Adding_A_Column()
    {
        var grid = await SeedGridAsync(1, 3, colWeights: [3, 1, 1], colLocked: [true, false, true]);
        var before = GridStructure.From((await _fx.GridRepository.FindByIdAsync(grid.Id))!);

        await _useCase.ExecuteAsync(grid.Id, before.WithColumnAdded());

        var stored = (await _fx.GridRepository.FindByIdAsync(grid.Id))!;
        stored.ColWeights.Should().Equal(3, 1, 1, 2);
        stored.ColLocked.Should().Equal(true, false, true, false);
    }

    [Fact]
    public async Task Undo_Direction_Restores_The_Original_Structure()
    {
        var grid = await SeedGridAsync(2, 2);
        var before = GridStructure.From((await _fx.GridRepository.FindByIdAsync(grid.Id))!);
        await _useCase.ExecuteAsync(grid.Id, before.WithColumnAdded());

        (await _useCase.ExecuteAsync(grid.Id, before)).IsError.Should().BeFalse();

        var stored = (await _fx.GridRepository.FindByIdAsync(grid.Id))!;
        stored.GridCols.Should().Be(2);
        stored.ColWeights.Should().Equal(1, 1);
    }

    [Fact]
    public async Task Shrinking_Below_A_Placement_Is_Rejected_And_Changes_Nothing()
    {
        var grid = await SeedGridAsync(2, 3);
        await PlaceAsync(grid, 2, 0); // 右端の列に配置がある
        var current = GridStructure.From((await _fx.GridRepository.FindByIdAsync(grid.Id))!);
        var shrunk = current with { Cols = 2, ColWeights = [1, 1], ColLocked = [false, false] };

        var result = await _useCase.ExecuteAsync(grid.Id, shrunk);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Grid.PlacementsOutOfRange");
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.GridCols.Should().Be(3);
    }

    [Fact]
    public async Task A_Multi_Cell_Placement_Counts_Toward_The_Bounds()
    {
        var grid = await SeedGridAsync(2, 3);
        await PlaceAsync(grid, 1, 0, new OccupySize(2, 1)); // 列 1〜2 を占有
        var current = GridStructure.From((await _fx.GridRepository.FindByIdAsync(grid.Id))!);
        var shrunk = current with { Cols = 2, ColWeights = [1, 1], ColLocked = [false, false] };

        (await _useCase.ExecuteAsync(grid.Id, shrunk)).IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_More_Than_The_Maximum_Dimensions()
    {
        var grid = await SeedGridAsync(UpdateGridStructureUseCase.MaxGridDimension, 2);
        var current = GridStructure.From((await _fx.GridRepository.FindByIdAsync(grid.Id))!);

        var result = await _useCase.ExecuteAsync(grid.Id, current.WithRowAdded());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Grid.RowsOutOfRange");
    }

    [Fact]
    public async Task Rejects_A_Structure_Whose_Arrays_Do_Not_Match_The_Counts()
    {
        var grid = await SeedGridAsync(2, 2);
        var bad = new GridStructure(2, 3, [1, 1], [1, 1], [false, false, false], [false, false]);

        var result = await _useCase.ExecuteAsync(grid.Id, bad);

        result.FirstError.Code.Should().Be("Grid.StructureMismatch");
    }

    [Fact]
    public async Task Returns_NotFound_For_An_Unknown_Grid()
    {
        var s = new GridStructure(1, 1, [1], [1], [false], [false]);

        (await _useCase.ExecuteAsync(Guid.NewGuid(), s)).FirstError.Code.Should().Be("Grid.NotFound");
    }
}
