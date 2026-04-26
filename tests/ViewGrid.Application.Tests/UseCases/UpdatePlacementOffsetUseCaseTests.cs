using System;
using System.Threading.Tasks;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdatePlacementOffsetUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdatePlacementOffsetUseCase _useCase = null!;
    private PlaceImageCopyUseCase _place = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        _place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Updates_PixelOffset_Without_Touching_Other_Fields()
    {
        var (placementId, gridId, copyId) = await SeedPlacementAsync();

        var result = await _useCase.ExecuteAsync(placementId, pixelOffsetX: 5, pixelOffsetY: -3);

        result.IsError.Should().BeFalse();

        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placementId);
        reloaded.Should().NotBeNull();
        reloaded!.PixelOffsetX.Should().Be(5);
        reloaded.PixelOffsetY.Should().Be(-3);
        reloaded.GridId.Should().Be(gridId);
        reloaded.CopyId.Should().Be(copyId);
        reloaded.Position.Should().Be(new CellPosition(0, 0));
    }

    [Fact]
    public async Task Allows_Negative_And_Zero_Offsets()
    {
        var (placementId, _, _) = await SeedPlacementAsync();

        var negative = await _useCase.ExecuteAsync(placementId, -100, -200);
        negative.IsError.Should().BeFalse();

        var zero = await _useCase.ExecuteAsync(placementId, 0, 0);
        zero.IsError.Should().BeFalse();

        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placementId);
        reloaded!.PixelOffsetX.Should().Be(0);
        reloaded.PixelOffsetY.Should().Be(0);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Placement()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), 10, 10);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    private async Task<(Guid PlacementId, Guid GridId, Guid CopyId)> SeedPlacementAsync()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
            GridRows = 2,
            GridCols = 2,
            ColWeights = GridCanvas.UniformWeights(2),
            RowWeights = GridCanvas.UniformWeights(2),
            CanvasSize = new PixelSize(200, 200),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();

        var p = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        p.IsError.Should().BeFalse();
        return (p.Value.Id, grid.Id, copy.Id);
    }
}
