using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class CreateGridCanvasUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private CreateGridCanvasUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new CreateGridCanvasUseCase(_fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Creates_Grid_With_Given_Dimensions()
    {
        // 起動時の自動選択は AppSettings.LastOpenedGridId 経由 (UI 層の責務)。
        var result = await _useCase.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "メイン",
            Rows = 3,
            Cols = 4,
            CanvasWidth = 1600,
            CanvasHeight = 1200,
        });

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("メイン");
        result.Value.GridRows.Should().Be(3);
        result.Value.GridCols.Should().Be(4);
        result.Value.CanvasSize.Width.Should().Be(1600);
        result.Value.CanvasSize.Height.Should().Be(1200);
    }

    [Fact]
    public async Task Trims_Name_Whitespace()
    {
        var result = await _useCase.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "  trimmed  ",
            Rows = 2, Cols = 2, CanvasWidth = 400, CanvasHeight = 400,
        });

        result.Value.Name.Should().Be("trimmed");
    }

    [Theory]
    [InlineData("", 3, 3, 400, 400)]
    [InlineData("ok", 0, 3, 400, 400)]
    [InlineData("ok", 21, 3, 400, 400)]
    [InlineData("ok", 3, 0, 400, 400)]
    [InlineData("ok", 3, 21, 400, 400)]
    [InlineData("ok", 3, 3, 0, 400)]
    [InlineData("ok", 3, 3, 8193, 400)]
    [InlineData("ok", 3, 3, 400, 0)]
    [InlineData("ok", 3, 3, 400, 8193)]
    public async Task Returns_Validation_Error_For_Invalid_Inputs(
        string name, int rows, int cols, int width, int height)
    {
        var result = await _useCase.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = name, Rows = rows, Cols = cols, CanvasWidth = width, CanvasHeight = height,
        });

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }
}
