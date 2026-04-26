using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Infrastructure.Imaging;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class ExportGridUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private ExportGridUseCase _export = null!;
    private DirectoryInfo _outputDir = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        var render = new RenderGridUseCase(
            _fx.GridRepository,
            _fx.PlacementRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _fx.Storage,
            new SkiaGridImageRenderer());
        _export = new ExportGridUseCase(render);
        _outputDir = TestImageFactory.CreateTempDirectory();
    }

    public async Task DisposeAsync()
    {
        if (_outputDir.Exists)
            _outputDir.Delete(recursive: true);
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task Writes_PNG_File_And_Returns_Result_With_Path_And_Size()
    {
        var grid = await SeedGridAsync(rows: 1, cols: 1, canvasSize: new PixelSize(50, 50));
        var outputPath = Path.Combine(_outputDir.FullName, "out.png");

        var result = await _export.ExecuteAsync(grid.Id, outputPath);

        result.IsError.Should().BeFalse();
        result.Value.Path.Should().Be(outputPath);
        result.Value.FileSizeBytes.Should().BeGreaterThan(0);
        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().Be(result.Value.FileSizeBytes);
    }

    [Fact]
    public async Task Creates_Missing_Parent_Directories()
    {
        var grid = await SeedGridAsync(rows: 1, cols: 1, canvasSize: new PixelSize(50, 50));
        var nested = Path.Combine(_outputDir.FullName, "a", "b", "c", "out.png");

        var result = await _export.ExecuteAsync(grid.Id, nested);

        result.IsError.Should().BeFalse();
        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public async Task Propagates_Render_Error_When_Grid_Missing()
    {
        var outputPath = Path.Combine(_outputDir.FullName, "should-not-exist.png");

        var result = await _export.ExecuteAsync(Guid.NewGuid(), outputPath);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public async Task Returns_Validation_Error_For_Empty_Output_Path()
    {
        var grid = await SeedGridAsync(rows: 1, cols: 1, canvasSize: new PixelSize(50, 50));

        var result = await _export.ExecuteAsync(grid.Id, "");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols, PixelSize canvasSize)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"export-{rows}x{cols}",
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
