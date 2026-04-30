using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class SwapPlacementsUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private PlaceImageCopyUseCase _placeUseCase = null!;
    private SwapPlacementsUseCase _swapUseCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _placeUseCase = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _swapUseCase = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Swaps_Positions_Of_Two_OneByOne_Placements()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id, copyName: "A");
        var copyB = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        var a = await _placeUseCase.ExecuteAsync(grid.Id, copyA.Id, new CellPosition(0, 0));
        var b = await _placeUseCase.ExecuteAsync(grid.Id, copyB.Id, new CellPosition(2, 2));

        var result = await _swapUseCase.ExecuteAsync(a.Value.Id, b.Value.Id);

        result.IsError.Should().BeFalse();

        var reloadA = await _fx.PlacementRepository.FindByIdAsync(a.Value.Id);
        var reloadB = await _fx.PlacementRepository.FindByIdAsync(b.Value.Id);
        reloadA!.Position.Should().Be(new CellPosition(2, 2));
        reloadB!.Position.Should().Be(new CellPosition(0, 0));
    }

    [Fact]
    public async Task Same_Id_Is_Noop()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var p = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 1));

        var result = await _swapUseCase.ExecuteAsync(p.Value.Id, p.Value.Id);

        result.IsError.Should().BeFalse();
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(p.Value.Id);
        reloaded!.Position.Should().Be(new CellPosition(1, 1));
    }

    [Fact]
    public async Task Swaps_NxM_Placements_When_New_Positions_Fit()
    {
        var grid = await SeedGridAsync(4, 4);
        var asset = await _fx.SeedAssetAsync();

        var copyOne = await _fx.SeedCopyAsync(asset.Id, copyName: "1x1");
        var copyTwo = await SeedCopyWithSizeAsync(asset.Id, "2x1", new OccupySize(2, 1));

        var a = await _placeUseCase.ExecuteAsync(grid.Id, copyOne.Id, new CellPosition(0, 0));
        var b = await _placeUseCase.ExecuteAsync(grid.Id, copyTwo.Id, new CellPosition(2, 2));

        var result = await _swapUseCase.ExecuteAsync(a.Value.Id, b.Value.Id);

        result.IsError.Should().BeFalse();
        var reloadA = await _fx.PlacementRepository.FindByIdAsync(a.Value.Id);
        var reloadB = await _fx.PlacementRepository.FindByIdAsync(b.Value.Id);
        reloadA!.Position.Should().Be(new CellPosition(2, 2));
        reloadB!.Position.Should().Be(new CellPosition(0, 0));
    }

    [Fact]
    public async Task Returns_OutOfBounds_When_Swap_Pushes_NxM_Outside_Grid()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();

        // 2×2 の big を (0,0) に置き、1×1 の small を (2,2) に置く。
        // swap すると big は (2,2) → 範囲外（(2,2)-(3,3) は (3,*) (*,3) を含む）
        var bigCopy = await SeedCopyWithSizeAsync(asset.Id, "2x2", new OccupySize(2, 2));
        var smallCopy = await _fx.SeedCopyAsync(asset.Id, copyName: "1x1");

        var big = await _placeUseCase.ExecuteAsync(grid.Id, bigCopy.Id, new CellPosition(0, 0));
        var small = await _placeUseCase.ExecuteAsync(grid.Id, smallCopy.Id, new CellPosition(2, 2));

        var result = await _swapUseCase.ExecuteAsync(big.Value.Id, small.Value.Id);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_Conflict_When_Swap_NxM_Hits_Other_Placement()
    {
        var grid = await SeedGridAsync(4, 4);
        var asset = await _fx.SeedAssetAsync();

        // 障害物: 1×1 を (0,2) に置く
        var blockerCopy = await _fx.SeedCopyAsync(asset.Id, copyName: "blocker");
        await _placeUseCase.ExecuteAsync(grid.Id, blockerCopy.Id, new CellPosition(0, 2));

        // a (1×1) を (3,3)、b (2×1) を (0,0) に置く。
        // swap で a → (0,0) (1×1)、b → (3,3) → (3,3)-(4,3) で範囲外なのでまず OutOfBounds。
        // 障害物との衝突を作るため、別レイアウトに変更する：
        // a (2×1) を (0,0)-(1,0)、 b (2×1) を (2,2)-(3,2) に置き、blocker を (1,2) に置く。
        // swap で a → (2,2)-(3,2) (元 b の位置)、b → (0,0)-(1,0) (元 a の位置)。
        // ↑ blocker (1,2) は b → (0,0)-(1,0) と重ならないので OK となる…
        // 別レイアウトを練り直す: blocker を (3,0) に置く。
        // a (2×1) at (0,0), b (2×1) at (1,2) → swap で a → (1,2)-(2,2), b → (0,0)-(1,0)
        // a の新位置 (1,2)-(2,2) は blocker (3,0) と無関係。失敗ケースを作るのは
        // 既存配置との衝突より、"互いに重複" の方が再現しやすい。

        var aCopy = await SeedCopyWithSizeAsync(asset.Id, "2x1-A", new OccupySize(2, 1));
        var cCopy = await SeedCopyWithSizeAsync(asset.Id, "1x1-C", new OccupySize(1, 1));

        var a = await _placeUseCase.ExecuteAsync(grid.Id, aCopy.Id, new CellPosition(0, 0));
        var c = await _placeUseCase.ExecuteAsync(grid.Id, cCopy.Id, new CellPosition(1, 1));
        // a (2×1) を (0,0)-(1,0)、c (1×1) を (1,1) に置く。
        // swap で a → (1,1)-(2,1)、c → (0,0)
        // 障害物として (2,1) に 1×1 を置く
        var blockerCopy2 = await SeedCopyWithSizeAsync(asset.Id, "blocker2", new OccupySize(1, 1));
        await _placeUseCase.ExecuteAsync(grid.Id, blockerCopy2.Id, new CellPosition(2, 1));

        var result = await _swapUseCase.ExecuteAsync(a.Value.Id, c.Value.Id);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    private async Task<ImageCopy> SeedCopyWithSizeAsync(Guid assetId, string copyName, OccupySize size)
    {
        var now = DateTimeOffset.UtcNow;
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            CopyName = copyName,
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = size,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var added = await _fx.CopyRepository.AddAsync(copy);
        if (added.IsError) throw new InvalidOperationException();
        return added.Value;
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Placements()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var p = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _swapUseCase.ExecuteAsync(p.Value.Id, Guid.NewGuid());

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"テスト {rows}x{cols}",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(800, 800),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        if (added.IsError) throw new InvalidOperationException();
        return added.Value;
    }
}
