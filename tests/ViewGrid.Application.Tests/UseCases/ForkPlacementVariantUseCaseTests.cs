using System.Collections.Immutable;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

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

    [Fact]
    public async Task Fork_Copies_ProtectedRegions_With_New_Ids_And_Fk()
    {
        // ★ IO-3 是正の anchor: 保護領域つきバリアントを Fork すると Regions も複製される。
        // 旧実装は CloneWithNewId が Regions を落とし、保護領域が静かに消えていた
        // (BOM IMAGE_VARIANT IO-3/ID-8)。各 Region は新 Id + 新 ImageCopyId の独立インスタンスで、
        // source と Id / FK / インスタンスを共有しない (片方の編集がもう片方へ漏れない)。
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var sourceCopy = await SeedCopyWithRegionsAsync(asset.Id);
        var placement = await SeedPlacementAsync(grid.Id, sourceCopy.Id);

        var result = await _useCase.ExecuteAsync(placement.Id);

        result.IsError.Should().BeFalse();
        var newCopyId = result.Value.NewCopyId;

        var newCopy = await _fx.CopyRepository.FindByIdAsync(newCopyId);
        newCopy.Should().NotBeNull();

        // 2 つの region が複製され、SortOrder 順 (内容) が保持される
        newCopy!.Regions.Should().HaveCount(2);
        newCopy.Regions.Select(r => r.SortOrder).Should().Equal(0, 1);

        // 内容は引き継ぐが、Id は新規、ImageCopyId は新バリアントを指す
        var sourceRegions = sourceCopy.Regions.OrderBy(r => r.SortOrder).ToArray();
        var newRegions = newCopy.Regions.OrderBy(r => r.SortOrder).ToArray();
        for (var i = 0; i < newRegions.Length; i++)
        {
            var src = sourceRegions[i];
            var dst = newRegions[i];
            dst.Id.Should().NotBe(src.Id);            // 新しい Id (source と共有しない)
            dst.ImageCopyId.Should().Be(newCopyId);   // FK は新バリアントを指す
            dst.Rect.Should().Be(src.Rect);
            dst.FillMode.Should().Be(src.FillMode);
            dst.FillColor.Should().Be(src.FillColor);
            dst.OffsetXPx.Should().Be(src.OffsetXPx);
            dst.OffsetYPx.Should().Be(src.OffsetYPx);
            dst.Rotation.Should().Be(src.Rotation);
            dst.FlipX.Should().Be(src.FlipX);
            dst.FlipY.Should().Be(src.FlipY);
            dst.SortOrder.Should().Be(src.SortOrder);
        }

        // 元バリアントの Regions は無傷 (Id も FK も元のまま) = 片方への漏れ無し
        var origStill = await _fx.CopyRepository.FindByIdAsync(sourceCopy.Id);
        origStill!.Regions.Should().HaveCount(2);
        origStill.Regions.Select(r => r.Id).Should()
            .BeEquivalentTo(sourceRegions.Select(r => r.Id));
        origStill.Regions.Should().OnlyContain(r => r.ImageCopyId == sourceCopy.Id);

        // 新旧で region Id が一切重ならない (インスタンス / Id 共有なし)
        newCopy.Regions.Select(r => r.Id).Should()
            .NotIntersectWith(origStill.Regions.Select(r => r.Id));
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
            OccupySize = OccupySize.OneByOne,
            PixelOffsetX = 0,
            PixelOffsetY = 0,
            PlacementOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.PlacementRepository.AddAsync(placement);
        return added.Value;
    }

    /// <summary>2 つの <see cref="ProtectedRegion"/> を持つ <see cref="ImageCopy"/> を投入する。</summary>
    private async Task<ImageCopy> SeedCopyWithRegionsAsync(Guid assetId)
    {
        var now = DateTimeOffset.UtcNow;
        var copyId = Guid.NewGuid();
        var copy = new ImageCopy
        {
            Id = copyId,
            AssetId = assetId,
            CopyName = "領域つき",
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
            Regions = ImmutableArray.Create(
                new ProtectedRegion
                {
                    Id = Guid.NewGuid(),
                    ImageCopyId = copyId,
                    Rect = new RegionRectFraction(0.1, 0.1, 0.3, 0.3),
                    FillMode = ProtectedRegionFillMode.White,
                    FillColor = null,
                    OffsetXPx = 5,
                    OffsetYPx = -3,
                    Rotation = Rotation.Cw90,
                    FlipX = true,
                    FlipY = false,
                    SortOrder = 0,
                },
                new ProtectedRegion
                {
                    Id = Guid.NewGuid(),
                    ImageCopyId = copyId,
                    Rect = new RegionRectFraction(0.5, 0.5, 0.2, 0.25),
                    FillMode = ProtectedRegionFillMode.Custom,
                    FillColor = 0xFF112233,
                    OffsetXPx = 0,
                    OffsetYPx = 0,
                    Rotation = Rotation.None,
                    FlipX = false,
                    FlipY = true,
                    SortOrder = 1,
                }),
        };

        var result = await _fx.CopyRepository.AddAsync(copy);
        if (result.IsError)
            throw new InvalidOperationException(
                $"SeedCopyWithRegionsAsync failed: {string.Join(", ", result.Errors)}");
        return copy;
    }
}
