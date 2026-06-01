using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class MovePlacementUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private PlaceImageCopyUseCase _placeUseCase = null!;
    private MovePlacementUseCase _moveUseCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _placeUseCase = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _moveUseCase = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Moves_Placement_To_Empty_Cell()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(2, 2));

        result.IsError.Should().BeFalse();
        result.Value.Position.Should().Be(new CellPosition(2, 2));

        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placed.Value.Id);
        reloaded!.Position.Should().Be(new CellPosition(2, 2));
    }

    [Fact]
    public async Task Returns_Conflict_When_Moving_Onto_Another_Placement()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id);
        var copyB = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        var a = await _placeUseCase.ExecuteAsync(grid.Id, copyA.Id, new CellPosition(0, 0));
        var b = await _placeUseCase.ExecuteAsync(grid.Id, copyB.Id, new CellPosition(1, 0));

        var result = await _moveUseCase.ExecuteAsync(a.Value.Id, b.Value.Position);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task Returns_OutOfBounds_When_Moving_Beyond_Grid()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(3, 0));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Same_Position_Is_Noop_And_Succeeds()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 1));

        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(1, 1));

        result.IsError.Should().BeFalse();
        result.Value.Position.Should().Be(new CellPosition(1, 1));
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Placement()
    {
        var result = await _moveUseCase.ExecuteAsync(Guid.NewGuid(), new CellPosition(0, 0));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    // AR-02 自己除外 anchor (Capability BOM governance pilot, F-P7):
    // N×M 配置を「自分の現 footprint と重なる位置」へ移動できることを保証する。
    // MovePlacementUseCase は PlacementValidator に excludePlacementId を渡して移動対象自身を
    // 除外する (AR-02 の「対象自身は除外」節)。この引数を落とすと、移動先が自分の旧占有と
    // 重なる限り自己重複で誤 Conflict になる。盲点: 既存 Move テストは全て 1×1 で自己重複が
    // 起きず、PlacementValidatorTests の自己除外テストは validator 単体 (引数を直接渡す) なので、
    // 「Move UseCase が実際に excludePlacementId を配線しているか」は本テストまで無被覆だった
    // (F-P2/maintenance-task-2 の候補B が突いた配線ギャップ)。
    [Fact]
    public async Task Allows_Move_That_Overlaps_Own_Current_Footprint()
    {
        var grid = await SeedGridAsync(3, 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await SeedCopyWithSizeAsync(asset.Id, new OccupySize(2, 1));  // 2×1
        var placed = await _placeUseCase.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();   // footprint {(0,0),(1,0)}

        // (0,0)→(1,0): 新 footprint {(1,0),(2,0)} は旧 {(0,0),(1,0)} と (1,0) で重なる。
        // 自己除外があれば成功、無ければ自己との重複で誤 Conflict。
        var result = await _moveUseCase.ExecuteAsync(placed.Value.Id, new CellPosition(1, 0));

        result.IsError.Should().BeFalse();
        result.Value.Position.Should().Be(new CellPosition(1, 0));
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(placed.Value.Id);
        reloaded!.Position.Should().Be(new CellPosition(1, 0));
    }

    private async Task<ImageCopy> SeedCopyWithSizeAsync(Guid assetId, OccupySize size, string copyName = "sized")
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
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        if (added.IsError) throw new InvalidOperationException();
        return added.Value;
    }
}
