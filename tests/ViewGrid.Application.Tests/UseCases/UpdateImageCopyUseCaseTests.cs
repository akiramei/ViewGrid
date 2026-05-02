using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdateImageCopyUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdateImageCopyUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Applies_Only_Provided_Fields_And_Preserves_Others()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "initial");

        var changes = new UpdateImageCopyChanges
        {
            ScalingMode = ScalingMode.UniformContainShrinkOnly,
            Alignment = new Alignment(AnchorX.Right, AnchorY.Bottom),
        };

        var result = await _useCase.ExecuteAsync(original.Id, changes);

        result.IsError.Should().BeFalse();
        var updated = result.Value;
        updated.Id.Should().Be(original.Id);
        updated.AssetId.Should().Be(original.AssetId);
        updated.ScalingMode.Should().Be(ScalingMode.UniformContainShrinkOnly);
        updated.Alignment.X.Should().Be(AnchorX.Right);
        updated.Alignment.Y.Should().Be(AnchorY.Bottom);

        // 未指定のフィールドは据え置き
        updated.CopyName.Should().Be("initial");
        updated.Transform.Should().Be(ImageTransform.Identity);
        updated.OccupySize.Should().Be(OccupySize.OneByOne);
    }

    [Fact]
    public async Task Updates_UpdatedAt_And_Preserves_CreatedAt()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id);

        // DateTimeOffset.UtcNow の Windows での分解能は ~15.6ms のため、安全側で 50ms 待機
        await Task.Delay(50);

        var result = await _useCase.ExecuteAsync(
            original.Id,
            new UpdateImageCopyChanges { CopyName = "renamed" });

        result.IsError.Should().BeFalse();
        result.Value.CreatedAt.Should().Be(original.CreatedAt);
        result.Value.UpdatedAt.Should().BeAfter(original.UpdatedAt);
    }

    [Fact]
    public async Task Persists_Changes_So_Subsequent_Reads_See_Them()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id);

        await _useCase.ExecuteAsync(
            original.Id,
            new UpdateImageCopyChanges
            {
                Transform = new ImageTransform(Rotation.Cw180, FlipX: false, FlipY: true),
                OccupySize = new OccupySize(2, 3),
            });

        var reloaded = await _fx.CopyRepository.FindByIdAsync(original.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Transform.Rotation.Should().Be(Rotation.Cw180);
        reloaded.Transform.FlipY.Should().BeTrue();
        reloaded.OccupySize.Width.Should().Be(2);
        reloaded.OccupySize.Height.Should().Be(3);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Copy()
    {
        var result = await _useCase.ExecuteAsync(
            Guid.NewGuid(),
            new UpdateImageCopyChanges { CopyName = "ghost" });

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    // ─── OccupySize はバリアント単位のデフォルト値として扱われる（配置単位へ移管後）───
    //
    // 旧版では OccupySize 変更が当該 ImageCopy を参照する全 Placement に伝播し、
    // 検証エラーで拒否される設計だった。配置単位の固有特性へ移管された後は:
    //   - ImageCopy.OccupySize はバリアント新規作成時のデフォルト値として残る
    //   - 既存配置の占有セルには伝播しない（GridPlacement.OccupySize が独立に保持）
    //   - 検証は配置単位の UpdatePlacementOccupySizeUseCase 側で行う

    /// <summary>OccupySize 変更は既存 placement に伝播せず、バリアント側だけ更新される。</summary>
    [Fact]
    public async Task OccupySize_Update_Does_Not_Affect_Existing_Placements()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(rows: 3, cols: 3);
        // (2,2) に 1×1 で配置（旧設計では OccupySize=1×2 への変更で範囲外エラーが出ていた）
        var placement = await SeedPlacementAsync(grid.Id, copy.Id, 2, 2);

        // バリアント側の OccupySize を 1×2 に変更。新設計では既存 placement の
        // OccupySize は GridPlacement 側で独立保持されており、この変更で影響を受けない。
        var result = await _useCase.ExecuteAsync(
            copy.Id,
            new UpdateImageCopyChanges { OccupySize = new OccupySize(1, 2) });

        result.IsError.Should().BeFalse();
        result.Value.OccupySize.Should().Be(new OccupySize(1, 2));

        // 既存 placement の OccupySize は変わっていない（1×1 のまま）
        var reloadedPlacement = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        reloadedPlacement!.OccupySize.Should().Be(OccupySize.OneByOne);
    }

    private async Task<GridCanvas> SeedActiveGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"test-{rows}x{cols}",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(400, 400),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        if (added.IsError) throw new InvalidOperationException(string.Join(", ", added.Errors));
        return grid;
    }

    private async Task<GridPlacement> SeedPlacementAsync(
        Guid gridId, Guid copyId, int x, int y, OccupySize? occupy = null)
    {
        var p = new GridPlacement
        {
            Id = Guid.NewGuid(),
            GridId = gridId,
            CopyId = copyId,
            Position = new CellPosition(x, y),
            OccupySize = occupy ?? OccupySize.OneByOne,
            PlacementOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.PlacementRepository.AddAsync(p);
        if (added.IsError) throw new InvalidOperationException(string.Join(", ", added.Errors));
        return p;
    }
}
