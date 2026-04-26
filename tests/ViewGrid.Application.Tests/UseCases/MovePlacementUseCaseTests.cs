using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class MovePlacementUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private PlaceImageCopyUseCase _placeUseCase = null!;
    private MovePlacementUseCase _moveUseCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _placeUseCase = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _moveUseCase = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Moves_Placement_To_Empty_Cell()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(2, 2));

        result.IsError.Should().BeFalse();
        result.Value.Position.Should().Be(new CellPosition(2, 2));

        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placed.Value.Id);
        reloaded!.Position.Should().Be(new CellPosition(2, 2));
    }

    [Fact]
    public async Task Returns_Conflict_When_Moving_Onto_Another_Placement()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id);
        var copyB = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        var a = await _placeUseCase.ExecuteAsync(grid.Id, copyA.Id, new CellPosition(0, 0));
        var b = await _placeUseCase.ExecuteAsync(grid.Id, copyB.Id, new CellPosition(1, 0));

        var result = await _moveUseCase.ExecuteAsync(a.Value.Id, b.Value.Position);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task Returns_OutOfBounds_When_Moving_Beyond_Grid()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(3, 0));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Same_Position_Is_Noop_And_Succeeds()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 1));

        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(1, 1));

        result.IsError.Should().BeFalse();
        result.Value.Position.Should().Be(new CellPosition(1, 1));
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Placement()
    {
        var result = await _moveUseCase.ExecuteAsync(Guid.NewGuid(), new CellPosition(0, 0));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
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
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        if (added.IsError) throw new InvalidOperationException();
        return added.Value;
    }
}
