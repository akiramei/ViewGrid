using System;
using System.Threading.Tasks;
using FluentAssertions;
using SkiaSharp;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Infrastructure.Imaging;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class RenderGridUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private RenderGridUseCase _useCase = null!;
    private PlaceImageCopyUseCase _place = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new RenderGridUseCase(
            _fx.GridRepository,
            _fx.PlacementRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _fx.Storage,
            new SkiaGridImageRenderer(new AutoCropCache()));
        _place = new PlaceImageCopyUseCase(
            _fx.GridRepository,
            _fx.CopyRepository,
            _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Returns_NotFound_For_Missing_Grid()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid());
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Returns_Transparent_PNG_When_Grid_Has_No_Placements()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 2, canvasSize: new PixelSize(80, 80));

        var result = await _useCase.ExecuteAsync(grid.Id);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(80);
        rendered.Height.Should().Be(80);
        rendered.GetPixel(40, 40).Alpha.Should().Be(0);
    }

    [Fact]
    public async Task Renders_All_Placements_Into_Single_PNG()
    {
        var grid = await SeedGridAsync(rows: 1, cols: 2, canvasSize: new PixelSize(100, 50));
        var asset = await _fx.SeedAssetAsync(width: 50, height: 50);
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var p1 = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        var p2 = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 0));
        p1.IsError.Should().BeFalse();
        p2.IsError.Should().BeFalse();

        var result = await _useCase.ExecuteAsync(grid.Id);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(100);
        rendered.Height.Should().Be(50);
        // SeedAssetAsync の既定色は CornflowerBlue
        rendered.GetPixel(25, 25).Should().Be(SKColors.CornflowerBlue);
        rendered.GetPixel(75, 25).Should().Be(SKColors.CornflowerBlue);
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols, PixelSize canvasSize)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"render-{rows}x{cols}",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = canvasSize,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();
        return grid;
    }
}
