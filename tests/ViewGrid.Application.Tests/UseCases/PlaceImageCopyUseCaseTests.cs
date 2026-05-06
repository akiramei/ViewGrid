using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class PlaceImageCopyUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private PlaceImageCopyUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new PlaceImageCopyUseCase(
            _fx.GridRepository,
            _fx.CopyRepository,
            _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Places_Copy_To_Empty_Cell()
    {
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var result = await _useCase.ExecuteAsync(
            grid.Id, copy.Id, new CellPosition(1, 1));

        result.IsError.Should().BeFalse();
        result.Value.GridId.Should().Be(grid.Id);
        result.Value.CopyId.Should().Be(copy.Id);
        result.Value.Position.Should().Be(new CellPosition(1, 1));
        result.Value.PlacementOrder.Should().Be(1);
    }

    [Fact]
    public async Task Returns_OutOfBounds_When_Position_Exceeds_Grid()
    {
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var result = await _useCase.ExecuteAsync(
            grid.Id, copy.Id, new CellPosition(3, 0));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_Conflict_When_Cell_Already_Occupied()
    {
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var first = await _useCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        first.IsError.Should().BeFalse();

        var second = await _useCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));

        second.IsError.Should().BeTrue();
        second.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Grid_Or_Copy()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var missingGrid = await _useCase.ExecuteAsync(
            Guid.NewGuid(), copy.Id, new CellPosition(0, 0));
        missingGrid.IsError.Should().BeTrue();
        missingGrid.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);

        var grid = await SeedGridAsync(2, 2);
        var missingCopy = await _useCase.ExecuteAsync(
            grid.Id, Guid.NewGuid(), new CellPosition(0, 0));
        missingCopy.IsError.Should().BeTrue();
        missingCopy.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Increments_PlacementOrder_For_Each_Placement()
    {
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var a = await _useCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        var b = await _useCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 0));
        var c = await _useCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(2, 0));

        a.Value.PlacementOrder.Should().Be(1);
        b.Value.PlacementOrder.Should().Be(2);
        c.Value.PlacementOrder.Should().Be(3);
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"テスト {rows}x{cols}",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(800, 800),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        if (added.IsError)
            throw new InvalidOperationException("Seed grid failed");
        return added.Value;
    }
}
