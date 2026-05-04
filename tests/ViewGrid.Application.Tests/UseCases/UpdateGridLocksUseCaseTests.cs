using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdateGridLocksUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdateGridLocksUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdateGridLocksUseCase(_fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Updates_ColLocked_While_Preserving_Other_Fields()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);

        var result = await _useCase.ExecuteAsync(grid.Id, [false, true, false], rowLocked: null);

        result.IsError.Should().BeFalse();
        result.Value.ColLocked.SequenceEqual([false, true, false]).Should().BeTrue();
        result.Value.RowLocked.SequenceEqual(grid.RowLocked).Should().BeTrue();

        var reloaded = await _fx.GridRepository.FindByIdAsync(grid.Id);
        reloaded!.ColLocked.SequenceEqual([false, true, false]).Should().BeTrue();
    }

    [Fact]
    public async Task Updates_RowLocked()
    {
        var grid = await SeedGridAsync(rows: 3, cols: 2);
        var result = await _useCase.ExecuteAsync(grid.Id, colLocked: null, [true, false, true]);
        result.IsError.Should().BeFalse();
        result.Value.RowLocked.SequenceEqual([true, false, true]).Should().BeTrue();
    }

    [Fact]
    public async Task Returns_Validation_Error_When_Count_Mismatches()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);
        var result = await _useCase.ExecuteAsync(grid.Id, [true, false], rowLocked: null);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Grid()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), [false, true], rowLocked: null);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            ColLocked = GridCanvas.AllUnlocked(cols),
            RowLocked = GridCanvas.AllUnlocked(rows),
            CanvasSize = new PixelSize(600, 600),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();
        return grid;
    }
}
