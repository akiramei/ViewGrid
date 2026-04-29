using FluentAssertions;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.History;

/// <summary>
/// 複数 Command を組み合わせた統合シナリオで、Undo/Redo が一貫した状態遷移を生むことを検証する。
/// </summary>
public sealed class IntegrationScenarioTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UndoRedoService _history = null!;
    private PlaceImageCopyUseCase _place = null!;
    private RemovePlacementUseCase _remove = null!;
    private MovePlacementUseCase _move = null!;
    private UpdateImageCopyUseCase _updateCopy = null!;
    private UpdateGridWeightsUseCase _updateWeights = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _history = new UndoRedoService();
        _place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        _move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository);
        _updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
    }

    public async Task DisposeAsync()
    {
        _history.Dispose();
        await _fx.DisposeAsync();
    }

    /// <summary>
    /// 配置 → 特性編集 → 列幅変更 → 3 回 Undo → 3 回 Redo の round-trip。
    /// 各 Command が独立して逆操作され、最終状態が初期と一致することを確認。
    /// </summary>
    [Fact]
    public async Task Place_UpdateCopy_UpdateWeights_RoundTrips()
    {
        // 初期状態
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var initialColWeights = grid.ColWeights;
        var initialRotation = copy.Transform.Rotation;
        var initialPlacementCount = (await _fx.PlacementRepository.FindByGridIdAsync(grid.Id)).Count;

        // Step 1: 配置
        var place = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(1, 1), description: "配置: テスト");
        await _history.ExecuteAsync(place);

        // Step 2: 特性編集
        var copyAfterPlace = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        var beforeChanges = UpdateImageCopyCommand.SnapshotFrom(copyAfterPlace!);
        var afterChanges = beforeChanges with { Transform = new ImageTransform(Rotation.Cw180, false, false) };
        var updateCopy = new UpdateImageCopyCommand(_updateCopy, copy.Id, beforeChanges, afterChanges,
            description: "特性編集: テスト");
        await _history.ExecuteAsync(updateCopy);

        // Step 3: 列幅変更
        var afterCol = System.Collections.Immutable.ImmutableArray.Create(3, 1, 1);
        var weights = new UpdateGridWeightsCommand(
            _updateWeights, grid.Id, initialColWeights, grid.RowWeights, afterCol, grid.RowWeights,
            description: "列幅変更: テスト");
        await _history.ExecuteAsync(weights);

        // 中間検証: 全部反映されている
        (await _fx.PlacementRepository.FindByGridIdAsync(grid.Id)).Count.Should().Be(initialPlacementCount + 1);
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.Transform.Rotation.Should().Be(Rotation.Cw180);
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.ColWeights.Should().Equal(afterCol);
        _history.CanUndo.Should().BeTrue();
        _history.CanRedo.Should().BeFalse();

        // 3 回 Undo: 列幅 → 特性 → 配置 の順
        await _history.UndoAsync();
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.ColWeights.Should().Equal(initialColWeights);

        await _history.UndoAsync();
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.Transform.Rotation.Should().Be(initialRotation);

        await _history.UndoAsync();
        (await _fx.PlacementRepository.FindByGridIdAsync(grid.Id)).Count.Should().Be(initialPlacementCount);
        _history.CanUndo.Should().BeFalse();
        _history.CanRedo.Should().BeTrue();

        // 3 回 Redo: 配置 → 特性 → 列幅
        await _history.RedoAsync();
        (await _fx.PlacementRepository.FindByGridIdAsync(grid.Id)).Count.Should().Be(initialPlacementCount + 1);

        await _history.RedoAsync();
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.Transform.Rotation.Should().Be(Rotation.Cw180);

        await _history.RedoAsync();
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.ColWeights.Should().Equal(afterCol);
        _history.CanUndo.Should().BeTrue();
        _history.CanRedo.Should().BeFalse();
    }

    /// <summary>
    /// Place → Move 後に Undo すると Move の逆 → Place の逆 の順で適用される。
    /// </summary>
    [Fact]
    public async Task Place_Move_Undo_ReturnsToInitial()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        // Place at (0,0)
        var place = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(0, 0), description: "配置: テスト");
        await _history.ExecuteAsync(place);
        var placementId = place.CreatedPlacementId!.Value;

        // Move to (2,2)
        var move = new MovePlacementCommand(_move, grid.Id, placementId,
            new CellPosition(0, 0), new CellPosition(2, 2), description: "移動: テスト");
        await _history.ExecuteAsync(move);
        (await _fx.PlacementRepository.FindByIdAsync(placementId))!.Position.Should().Be(new CellPosition(2, 2));

        // Undo Move
        await _history.UndoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placementId))!.Position.Should().Be(new CellPosition(0, 0));

        // Undo Place
        await _history.UndoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placementId)).Should().BeNull();
    }

    /// <summary>
    /// 新しい操作を Execute すると Redo スタックがクリアされる。
    /// </summary>
    [Fact]
    public async Task Execute_New_After_Undo_Drops_Redo_Stack()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var first = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(0, 0), description: "配置: 1 回目");
        await _history.ExecuteAsync(first);
        await _history.UndoAsync();
        _history.CanRedo.Should().BeTrue();

        // 別 Place を Execute → Redo は破棄される
        var second = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(1, 1), description: "配置: 2 回目");
        await _history.ExecuteAsync(second);
        _history.CanRedo.Should().BeFalse();
        _history.CanUndo.Should().BeTrue();
    }

    private async Task<GridCanvas> SeedGridAsync(int rows = 3, int cols = 3)
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
        if (added.IsError)
            throw new InvalidOperationException("Seed grid failed");
        return added.Value;
    }
}
