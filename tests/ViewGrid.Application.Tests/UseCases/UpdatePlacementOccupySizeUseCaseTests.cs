using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

/// <summary>
/// 配置単位の OccupySize 編集 UseCase のテスト。
/// 旧 ImageCopy 単位の検証から「同じバリアントを別グリッドの別セルに配置している場合でも
/// 配置単位で独立して占有セルを変えられる」という新挙動を担保する。
/// </summary>
public sealed class UpdatePlacementOccupySizeUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdatePlacementOccupySizeUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Returns_NotFound_When_Placement_Missing()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), new OccupySize(2, 2));
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Placement.NotFound");
    }

    [Fact]
    public async Task Updates_OccupySize_When_Within_Bounds_And_No_Conflict()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var placement = await SeedPlacementAsync(grid.Id, copy.Id, 1, 1);

        // (1,1) で 1×2 → (1,1)-(1,2) で 3×3 に収まる
        var result = await _useCase.ExecuteAsync(placement.Id, new OccupySize(1, 2));

        result.IsError.Should().BeFalse();
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        reloaded!.OccupySize.Should().Be(new OccupySize(1, 2));
    }

    [Fact]
    public async Task Same_Variant_In_Different_Grids_Allows_Independent_OccupySize()
    {
        // 「同じバリアント A を 2 つのグリッドに配置している」シナリオ。
        // バックエンド側が配置固有として扱う設計を担保（旧設計では Grid 2 の境界外で
        // エラーが出ていた）。
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid1 = await SeedGridAsync(rows: 3, cols: 3);
        var grid2 = await SeedGridAsync(rows: 3, cols: 3);

        // Grid 1 の (1,0) と Grid 2 の (0,2) に同バリアントを配置
        var placementInGrid1 = await SeedPlacementAsync(grid1.Id, copy.Id, 1, 0);
        await SeedPlacementAsync(grid2.Id, copy.Id, 0, 2);

        // Grid 1 の placement だけ占有 1×2 にしたい。Grid 2 の (0,2) で 1×2 だと
        // 範囲外になるが、新設計では他グリッドへ伝播しないので Grid 1 単独で成立する。
        var result = await _useCase.ExecuteAsync(placementInGrid1.Id, new OccupySize(1, 2));

        result.IsError.Should().BeFalse();
        var reloaded1 = await _fx.PlacementRepository.FindByIdAsync(placementInGrid1.Id);
        reloaded1!.OccupySize.Should().Be(new OccupySize(1, 2));
    }

    [Fact]
    public async Task Returns_OutOfBounds_When_New_OccupySize_Exceeds_Grid()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var placement = await SeedPlacementAsync(grid.Id, copy.Id, 2, 2);

        // (2,2) で 1×2 → 行 3 が範囲外
        var result = await _useCase.ExecuteAsync(placement.Id, new OccupySize(1, 2));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Placement.OccupySizeOutOfBounds");
    }

    [Fact]
    public async Task Returns_Conflict_When_New_OccupySize_Overlaps_Other_Placement()
    {
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id, copyName: "A");
        var copyB = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var placementA = await SeedPlacementAsync(grid.Id, copyA.Id, 0, 0);
        await SeedPlacementAsync(grid.Id, copyB.Id, 1, 0);

        // A を 2×1 に変更 → (0,0)-(1,0) で B と重なる
        var result = await _useCase.ExecuteAsync(placementA.Id, new OccupySize(2, 1));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Placement.OccupySizeConflict");
    }

    [Fact]
    public async Task Same_OccupySize_Is_NoOp()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedGridAsync(rows: 1, cols: 1);
        var placement = await SeedPlacementAsync(grid.Id, copy.Id, 0, 0);

        // 既存と同じ 1×1
        var result = await _useCase.ExecuteAsync(placement.Id, OccupySize.OneByOne);

        result.IsError.Should().BeFalse();
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
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

    private async Task<GridPlacement> SeedPlacementAsync(Guid gridId, Guid copyId, int x, int y)
    {
        var p = new GridPlacement
        {
            Id = Guid.NewGuid(),
            GridId = gridId,
            CopyId = copyId,
            Position = new CellPosition(x, y),
            OccupySize = OccupySize.OneByOne,
            PlacementOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.PlacementRepository.AddAsync(p);
        if (added.IsError) throw new InvalidOperationException(string.Join(", ", added.Errors));
        return p;
    }
}
