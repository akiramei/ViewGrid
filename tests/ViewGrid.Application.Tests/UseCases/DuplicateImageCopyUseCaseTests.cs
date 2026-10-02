using System.Collections.Immutable;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class DuplicateImageCopyUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private DuplicateImageCopyUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new DuplicateImageCopyUseCase(_fx.CopyRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    /// <summary>crop・回転・縦揃え・保護領域まで設定済みのバリアントを用意する。</summary>
    private async Task<ImageCopy> SeedConfiguredCopyAsync()
    {
        var asset = await _fx.SeedAssetAsync(width: 200, height: 100);
        var id = Guid.NewGuid();
        var copy = new ImageCopy
        {
            Id = id,
            AssetId = asset.Id,
            CopyName = "人物",
            Transform = new ImageTransform(Rotation.Cw90, FlipX: true, FlipY: false),
            ScalingMode = ScalingMode.UniformCover,
            Alignment = new Alignment(AnchorX.Left, AnchorY.Bottom),
            OccupySize = new OccupySize(2, 1),
            ManualCrop = new ManualCropFraction(0.1, 0.2, 0.5, 0.4),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Regions = ImmutableArray.Create(new ProtectedRegion
            {
                Id = Guid.NewGuid(),
                ImageCopyId = id,
                Rect = new RegionRectFraction(0.1, 0.1, 0.2, 0.2),
                FillMode = ProtectedRegionFillMode.Custom,
                FillColor = 0xFF112233u,
                OffsetXPx = 7,
                OffsetYPx = 9,
                Rotation = Rotation.Cw180,
                SortOrder = 0,
            }),
        };
        (await _fx.CopyRepository.AddAsync(copy)).IsError.Should().BeFalse();
        return copy;
    }

    [Fact]
    public async Task Duplicates_Every_Setting_Into_A_New_Variant()
    {
        var source = await SeedConfiguredCopyAsync();

        var result = await _useCase.ExecuteAsync(source.Id, "人物 (複製)");

        result.IsError.Should().BeFalse();
        var dup = (await _fx.CopyRepository.FindByIdAsync(result.Value.Id))!;
        dup.Id.Should().NotBe(source.Id);
        dup.AssetId.Should().Be(source.AssetId);
        dup.CopyName.Should().Be("人物 (複製)");
        dup.Transform.Should().Be(source.Transform);
        dup.ScalingMode.Should().Be(source.ScalingMode);
        dup.Alignment.Should().Be(source.Alignment);
        dup.OccupySize.Should().Be(source.OccupySize);
        dup.ManualCrop.Should().Be(source.ManualCrop);
        dup.Regions.Should().HaveCount(1);
        dup.Regions[0].Rect.Should().Be(source.Regions[0].Rect);
        dup.Regions[0].FillColor.Should().Be(0xFF112233u);
        dup.Regions[0].OffsetXPx.Should().Be(7);
        dup.Regions[0].Rotation.Should().Be(Rotation.Cw180);
    }

    [Fact]
    public async Task The_Copy_Is_Independent_Of_The_Original_Including_Region_Identity()
    {
        var source = await SeedConfiguredCopyAsync();

        var dup = (await _useCase.ExecuteAsync(source.Id, "dup")).Value;

        var reloadedSource = (await _fx.CopyRepository.FindByIdAsync(source.Id))!;
        reloadedSource.CopyName.Should().Be("人物", "元のバリアントは変わらない");
        reloadedSource.Regions.Should().HaveCount(1);
        var storedDup = (await _fx.CopyRepository.FindByIdAsync(dup.Id))!;
        storedDup.Regions[0].Id.Should().NotBe(source.Regions[0].Id, "保護領域は独立した新しい Id");
        storedDup.Regions[0].ImageCopyId.Should().Be(dup.Id);
    }

    [Fact]
    public async Task Does_Not_Touch_Existing_Placements_Of_The_Original()
    {
        var source = await SeedConfiguredCopyAsync();
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(), Name = "g", GridRows = 2, GridCols = 2,
            ColWeights = GridCanvas.UniformWeights(2), RowWeights = GridCanvas.UniformWeights(2),
            CanvasSize = new PixelSize(400, 400),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placement = (await place.ExecuteAsync(grid.Id, source.Id, new CellPosition(0, 0))).Value;

        await _useCase.ExecuteAsync(source.Id, "dup");

        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.CopyId.Should().Be(source.Id,
            "複製は配置を付け替えない (配置 1 件だけを分岐する Fork とは別)");
    }

    [Fact]
    public async Task Blank_Name_Makes_An_Unnamed_Copy()
    {
        var source = await SeedConfiguredCopyAsync();

        var dup = (await _useCase.ExecuteAsync(source.Id, "   ")).Value;

        (await _fx.CopyRepository.FindByIdAsync(dup.Id))!.CopyName.Should().BeNull();
    }

    [Fact]
    public async Task Returns_NotFound_For_An_Unknown_Source()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), "dup");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ImageCopy.NotFound");
    }
}
