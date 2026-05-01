using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

/// <summary>
/// <see cref="ForkPlacementVariantUseCase"/> の単体テスト。fork による独立化が
/// (1) 元バリアントを変えず、(2) 新規 ImageCopy を作り、
/// (3) 当該 Placement の CopyId のみ付け替える、ことを検証する。
/// </summary>
public sealed class ForkPlacementVariantUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private ForkPlacementVariantUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new ForkPlacementVariantUseCase(_fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Forks_Placement_To_New_Variant_With_Same_Properties()
    {
        // Arrange: アセット → バリアント → 配置 をシード。バリアント名は明示
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var sourceCopy = await _fx.SeedCopyAsync(asset.Id, copyName: "元バリアント");
        var placement = await SeedPlacementAsync(grid.Id, sourceCopy.Id);

        // Act
        var result = await _useCase.ExecuteAsync(placement.Id);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.OriginalCopyId.Should().Be(sourceCopy.Id);
        result.Value.NewCopyId.Should().NotBe(sourceCopy.Id);

        // 新規バリアントが DB に登録されている
        var newCopy = await _fx.CopyRepository.FindByIdAsync(result.Value.NewCopyId);
        newCopy.Should().NotBeNull();
        newCopy!.CopyName.Should().Be("元バリアント (派生)");
        newCopy.AssetId.Should().Be(asset.Id);
        newCopy.Transform.Should().Be(sourceCopy.Transform);
        newCopy.ScalingMode.Should().Be(sourceCopy.ScalingMode);
        newCopy.Alignment.Should().Be(sourceCopy.Alignment);
        newCopy.OccupySize.Should().Be(sourceCopy.OccupySize);

        // Placement の CopyId が付け替わっている
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        reloaded!.CopyId.Should().Be(result.Value.NewCopyId);

        // 元バリアントは残っている（他の配置に影響しない）
        var origStill = await _fx.CopyRepository.FindByIdAsync(sourceCopy.Id);
        origStill.Should().NotBeNull();
    }

    [Fact]
    public async Task Fork_Of_Unnamed_Variant_Uses_Default_Prefix()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var sourceCopy = await _fx.SeedCopyAsync(asset.Id, copyName: null);
        var placement = await SeedPlacementAsync(grid.Id, sourceCopy.Id);

        var result = await _useCase.ExecuteAsync(placement.Id);

        result.IsError.Should().BeFalse();
        var newCopy = await _fx.CopyRepository.FindByIdAsync(result.Value.NewCopyId);
        newCopy!.CopyName.Should().Be("バリアント (派生)");
    }

    [Fact]
    public async Task Returns_NotFound_When_Placement_Missing()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid());
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("GridPlacement.NotFound");
    }

    private async Task<GridCanvas> SeedGridAsync()
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
            GridRows = 2,
            GridCols = 2,
            ColWeights = GridCanvas.UniformWeights(2),
            RowWeights = GridCanvas.UniformWeights(2),
            CanvasSize = new PixelSize(400, 400),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        return added.Value;
    }

    private async Task<GridPlacement> SeedPlacementAsync(Guid gridId, Guid copyId)
    {
        var placement = new GridPlacement
        {
            Id = Guid.NewGuid(),
            GridId = gridId,
            CopyId = copyId,
            Position = new CellPosition(0, 0),
            PixelOffsetX = 0,
            PixelOffsetY = 0,
            PlacementOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.PlacementRepository.AddAsync(placement);
        return added.Value;
    }
}
