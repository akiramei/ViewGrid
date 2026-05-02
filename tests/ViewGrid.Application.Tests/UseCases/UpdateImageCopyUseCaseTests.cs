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

    // ─── OccupySize 変更時の検証（クラッシュ根本対策） ───
    //
    // 共有特性として OccupySize を変更すると、当該 ImageCopy を参照する全 Placement に
    // 即座に伝播する。グリッド外にはみ出したり既存配置と競合したりする状態を永続化すると、
    // View 再描画時にクラッシュする。UseCase 層で事前検証して拒否する。

    /// <summary>新 OccupySize で配置がグリッド外にはみ出す場合は OutOfBounds で拒否する。</summary>
    [Fact]
    public async Task Returns_OutOfBounds_When_New_OccupySize_Exceeds_Grid_From_Existing_Placement()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        // 3×3 グリッドの (2,2) に 1×1 で配置
        var grid = await SeedActiveGridAsync(rows: 3, cols: 3);
        await SeedPlacementAsync(grid.Id, copy.Id, 2, 2);

        // OccupySize を 1×2 に変更しようとすると、(2,2)-(2,3) で行 3 が範囲外
        var result = await _useCase.ExecuteAsync(
            copy.Id,
            new UpdateImageCopyChanges { OccupySize = new OccupySize(1, 2) });

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ImageCopy.OccupySizeOutOfBounds");
        // 永続化されていない
        var reloaded = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        reloaded!.OccupySize.Should().Be(OccupySize.OneByOne);
    }

    /// <summary>新 OccupySize で他配置と重なる場合は Conflict で拒否する。</summary>
    [Fact]
    public async Task Returns_Conflict_When_New_OccupySize_Overlaps_Other_Placement()
    {
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id, copyName: "A");
        var copyB = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        var grid = await SeedActiveGridAsync(rows: 3, cols: 3);
        await SeedPlacementAsync(grid.Id, copyA.Id, 0, 0);
        await SeedPlacementAsync(grid.Id, copyB.Id, 1, 0);

        // copyA を 2×1 に変更しようとすると (0,0)-(1,0) で copyB と重なる
        var result = await _useCase.ExecuteAsync(
            copyA.Id,
            new UpdateImageCopyChanges { OccupySize = new OccupySize(2, 1) });

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ImageCopy.OccupySizeConflict");
    }

    /// <summary>全配置がグリッド内かつ競合しなければ OccupySize 変更は成功する。</summary>
    [Fact]
    public async Task Allows_OccupySize_Change_When_All_Placements_Fit()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(rows: 3, cols: 3);
        await SeedPlacementAsync(grid.Id, copy.Id, 0, 0);

        // (0,0) で 2×2 ならグリッド内に収まり、他配置もないので成功
        var result = await _useCase.ExecuteAsync(
            copy.Id,
            new UpdateImageCopyChanges { OccupySize = new OccupySize(2, 2) });

        result.IsError.Should().BeFalse();
        result.Value.OccupySize.Width.Should().Be(2);
        result.Value.OccupySize.Height.Should().Be(2);
    }

    /// <summary>OccupySize が同一値（変更なし）なら検証はスキップして成功する。</summary>
    [Fact]
    public async Task Skips_Validation_When_OccupySize_Unchanged()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(rows: 1, cols: 1);
        await SeedPlacementAsync(grid.Id, copy.Id, 0, 0);

        // OccupySize を渡さず、CopyName のみ変更（既存値と同じ OccupySize 1×1 でも検証パスはスキップされる）
        var result = await _useCase.ExecuteAsync(
            copy.Id,
            new UpdateImageCopyChanges { CopyName = "renamed" });

        result.IsError.Should().BeFalse();
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
