using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdateGridCanvasSizeUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdateGridCanvasSizeUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdateGridCanvasSizeUseCase(_fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Updates_CanvasSize_While_Preserving_Other_Fields()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);

        var result = await _useCase.ExecuteAsync(grid.Id, 1920, 1080);

        result.IsError.Should().BeFalse();
        result.Value.CanvasSize.Should().Be(new PixelSize(1920, 1080));
        result.Value.GridRows.Should().Be(grid.GridRows);
        result.Value.GridCols.Should().Be(grid.GridCols);
        result.Value.ColWeights.SequenceEqual(grid.ColWeights).Should().BeTrue();
        result.Value.RowWeights.SequenceEqual(grid.RowWeights).Should().BeTrue();

        var reloaded = await _fx.GridRepository.FindByIdAsync(grid.Id);
        reloaded!.CanvasSize.Should().Be(new PixelSize(1920, 1080));
    }

    [Fact]
    public async Task Returns_Same_Instance_When_Size_Unchanged()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);

        var result = await _useCase.ExecuteAsync(grid.Id, grid.CanvasSize.Width, grid.CanvasSize.Height);

        result.IsError.Should().BeFalse();
        result.Value.CanvasSize.Should().Be(grid.CanvasSize);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(-100, 1080)]
    [InlineData(1920, 0)]
    [InlineData(1920, -100)]
    [InlineData(20000, 1080)] // > MaxSize
    [InlineData(1920, 20000)]
    public async Task Returns_Validation_Error_For_Invalid_Size(int width, int height)
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);
        var result = await _useCase.ExecuteAsync(grid.Id, width, height);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Grid()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), 1920, 1080);
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
            CanvasSize = new PixelSize(1200, 1200),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();
        return grid;
    }
}
