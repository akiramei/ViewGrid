using FluentAssertions;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.History.Commands;

/// <summary>
/// 配置系 5 Command（Place/Remove/Move/Swap/UpdatePlacementOffset）の Execute → Undo → Redo
/// round-trip を実 EF Core in-memory SQLite で検証する。
/// </summary>
public sealed class PlacementCommandTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private PlaceImageCopyUseCase _place = null!;
    private RemovePlacementUseCase _remove = null!;
    private MovePlacementUseCase _move = null!;
    private SwapPlacementsUseCase _swap = null!;
    private UpdatePlacementOffsetUseCase _offset = null!;
    private UndoRedoService _history = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        _move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _swap = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        _history = new UndoRedoService();
    }

    public async Task DisposeAsync()
    {
        _history.Dispose();
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task PlaceCommand_Execute_Undo_Redo_RoundTrip()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        var command = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(1, 1), description: "配置: テスト");

        // Execute → 配置作成
        var execResult = await _history.ExecuteAsync(command);
        execResult.IsError.Should().BeFalse();
        command.CreatedPlacementId.Should().NotBeNull();
        var createdId = command.CreatedPlacementId!.Value;
        (await _fx.PlacementRepository.FindByIdAsync(createdId)).Should().NotBeNull();

        // Undo → 配置削除
        var undoResult = await _history.UndoAsync();
        undoResult.IsError.Should().BeFalse();
        (await _fx.PlacementRepository.FindByIdAsync(createdId)).Should().BeNull();

        // Redo → 同じ Id で復活
        var redoResult = await _history.RedoAsync();
        redoResult.IsError.Should().BeFalse();
        var restored = await _fx.PlacementRepository.FindByIdAsync(createdId);
        restored.Should().NotBeNull();
        restored!.Position.Should().Be(new CellPosition(1, 1));
    }

    [Fact]
    public async Task RemovePlacementCommand_Restores_Full_State_Including_PixelOffset()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        // 配置 + PixelOffset 設定
        var placeResult = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placeResult.IsError.Should().BeFalse();
        var placement = placeResult.Value;
        await _offset.ExecuteAsync(placement.Id, 42, -77);

        // Snapshot 取得 → RemovePlacementCommand
        var snapshot = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        snapshot.Should().NotBeNull();
        snapshot!.PixelOffsetX.Should().Be(42);
        snapshot.PixelOffsetY.Should().Be(-77);

        var command = new RemovePlacementCommand(_remove, _fx.PlacementRepository, snapshot,
            description: "削除: テスト");

        // Execute → 削除
        await _history.ExecuteAsync(command);
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id)).Should().BeNull();

        // Undo → 同じ Id + PixelOffset で復活
        await _history.UndoAsync();
        var restored = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        restored.Should().NotBeNull();
        restored!.PixelOffsetX.Should().Be(42);
        restored.PixelOffsetY.Should().Be(-77);

        // Redo → 再削除
        await _history.RedoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id)).Should().BeNull();
    }

    [Fact]
    public async Task MovePlacementCommand_Reverts_To_Before_Position()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placement = (await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0))).Value;

        var command = new MovePlacementCommand(_move, grid.Id, placement.Id,
            before: new CellPosition(0, 0),
            after: new CellPosition(2, 2),
            description: "移動: テスト");

        await _history.ExecuteAsync(command);
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.Position.Should().Be(new CellPosition(2, 2));

        await _history.UndoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.Position.Should().Be(new CellPosition(0, 0));

        await _history.RedoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.Position.Should().Be(new CellPosition(2, 2));
    }

    [Fact]
    public async Task SwapPlacementsCommand_Symmetric_Execute_Undo_Redo()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id, "A");
        var copyB = await _fx.SeedCopyAsync(asset.Id, "B");
        var a = (await _place.ExecuteAsync(grid.Id, copyA.Id, new CellPosition(0, 0))).Value;
        var b = (await _place.ExecuteAsync(grid.Id, copyB.Id, new CellPosition(2, 2))).Value;

        var command = new SwapPlacementsCommand(_swap, grid.Id, a.Id, b.Id,
            description: "入れ替え: テスト");

        await _history.ExecuteAsync(command);
        (await _fx.PlacementRepository.FindByIdAsync(a.Id))!.Position.Should().Be(new CellPosition(2, 2));
        (await _fx.PlacementRepository.FindByIdAsync(b.Id))!.Position.Should().Be(new CellPosition(0, 0));

        await _history.UndoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(a.Id))!.Position.Should().Be(new CellPosition(0, 0));
        (await _fx.PlacementRepository.FindByIdAsync(b.Id))!.Position.Should().Be(new CellPosition(2, 2));

        await _history.RedoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(a.Id))!.Position.Should().Be(new CellPosition(2, 2));
        (await _fx.PlacementRepository.FindByIdAsync(b.Id))!.Position.Should().Be(new CellPosition(0, 0));
    }

    [Fact]
    public async Task UpdatePlacementOffsetCommand_RoundTrip()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placement = (await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0))).Value;

        var command = new UpdatePlacementOffsetCommand(_offset, grid.Id, placement.Id,
            beforeX: 0, beforeY: 0, afterX: 100, afterY: -50,
            description: "ピクセル微調整: テスト");

        await _history.ExecuteAsync(command);
        var afterExec = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        afterExec!.PixelOffsetX.Should().Be(100);
        afterExec.PixelOffsetY.Should().Be(-50);

        await _history.UndoAsync();
        var afterUndo = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        afterUndo!.PixelOffsetX.Should().Be(0);
        afterUndo.PixelOffsetY.Should().Be(0);

        await _history.RedoAsync();
        var afterRedo = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        afterRedo!.PixelOffsetX.Should().Be(100);
        afterRedo.PixelOffsetY.Should().Be(-50);
    }

    [Fact]
    public async Task PlaceCommand_AffectedGridId_Identifies_Target_Grid()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var command = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(0, 0),
            description: "配置: テストコピー → (0,0)");

        command.AffectedGridId.Should().Be(grid.Id);
        command.Description.Should().Be("配置: テストコピー → (0,0)");
    }

    [Fact]
    public async Task PlaceCommand_Failed_Execute_Does_Not_Push_To_Stack()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);

        // 同じ位置に 2 回目の配置 → Conflict
        var first = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(0, 0), description: "配置: 1 回目");
        await _history.ExecuteAsync(first);

        var second = new PlaceCommand(_place, _remove, _fx.PlacementRepository,
            grid.Id, copy.Id, new CellPosition(0, 0), description: "配置: 2 回目（衝突）");
        var result = await _history.ExecuteAsync(second);

        result.IsError.Should().BeTrue();
        // 失敗した Command はスタックに積まれない（first だけが残る）
        await _history.UndoAsync(); // first を undo
        _history.CanUndo.Should().BeFalse();
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
