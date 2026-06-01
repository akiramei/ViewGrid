using FluentAssertions;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.History.Commands;

/// <summary>
/// 配置系 6 Command（Place/Remove/Move/Swap/UpdatePlacementOffset/UpdatePlacementOccupySize）の
/// Execute → Undo → Redo round-trip を実 EF Core in-memory SQLite で検証する。
/// </summary>
public sealed class PlacementCommandTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private PlaceImageCopyUseCase _place = null!;
    private RemovePlacementUseCase _remove = null!;
    private MovePlacementUseCase _move = null!;
    private SwapPlacementsUseCase _swap = null!;
    private UpdatePlacementOffsetUseCase _offset = null!;
    private UpdatePlacementOccupySizeUseCase _occupy = null!;
    private UndoRedoService _history = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        _move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _swap = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        _offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        _occupy = new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository);
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

    // AR-07 undo 対称性 anchor (Capability BOM governance pilot, F-P6):
    // RemovePlacementCommand の「全フィールド snapshot 復元」のうち PlacementOrder (重なり順) を検証する。
    // 既存 RemovePlacementCommand_Restores_Full_State_Including_PixelOffset は名前に反し PixelOffset しか
    // 確認せず、PlacementOrder の復元は無被覆だった (PlacementOrder のテストは PlaceImageCopyUseCaseTests の
    // *作成時採番* と renderer/fork の seed のみで、削除→undo 復元は誰も見ていない)。
    // z-order は D-8 で実質「作成順」だが、削除→undo で順序が変わると重なり順が静かに崩れる
    // (AR-07: undo は削除前の正確な状態を復元すべき。「復元配置は最前面へ」等の善意の正規化は違反)。
    // 中間 order の配置を削除/復元することで、snapshot 値そのまま (=2) 以外 —
    // 最前面へ積み直し (=4) / 既定値 (0) / 再採番 — を決定的に弾く。
    [Fact]
    public async Task RemovePlacementCommand_Restores_PlacementOrder()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copyA = await _fx.SeedCopyAsync(asset.Id, "A");
        var copyB = await _fx.SeedCopyAsync(asset.Id, "B");
        var copyC = await _fx.SeedCopyAsync(asset.Id, "C");

        // 作成順に PlacementOrder = 1, 2, 3 が振られる (PlaceImageCopyUseCase: 空なら 1、以降 max+1)。
        var a = (await _place.ExecuteAsync(grid.Id, copyA.Id, new CellPosition(0, 0))).Value;
        var b = (await _place.ExecuteAsync(grid.Id, copyB.Id, new CellPosition(1, 0))).Value;
        var c = (await _place.ExecuteAsync(grid.Id, copyC.Id, new CellPosition(2, 0))).Value;

        // 前提: B は中間 order = 2 (a=1, c=3)。RemovePlacementUseCase は再採番しない (D-8)。
        var snapshot = await _fx.PlacementRepository.FindByIdAsync(b.Id);
        snapshot!.PlacementOrder.Should().Be(2);

        var command = new RemovePlacementCommand(_remove, _fx.PlacementRepository, snapshot,
            description: "削除: B");

        // Execute → 削除
        await _history.ExecuteAsync(command);
        (await _fx.PlacementRepository.FindByIdAsync(b.Id)).Should().BeNull();

        // Undo → 削除前の正確な PlacementOrder (=2) で復元されるべき。
        await _history.UndoAsync();
        var restored = await _fx.PlacementRepository.FindByIdAsync(b.Id);
        restored.Should().NotBeNull();
        restored!.PlacementOrder.Should().Be(2);

        // 他配置の重なり順も不変 (a=1, c=3)。
        (await _fx.PlacementRepository.FindByIdAsync(a.Id))!.PlacementOrder.Should().Be(1);
        (await _fx.PlacementRepository.FindByIdAsync(c.Id))!.PlacementOrder.Should().Be(3);
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

    // AR-07 undo 対称性 anchor (Capability BOM governance pilot, F-P5):
    // 配置 (GridPlacement) 固有 OccupySize の Execute→Undo→Redo 対称性を検証する。
    // BOM の AR-07 (fragile) は Move/UpdateOffset/UpdateOccupy を before/after 対称と宣言するが、
    // 兄弟 5 Command に round-trip テストがある一方、UpdatePlacementOccupySizeCommand だけ未被覆だった。
    // 注意: GridAndCopyCommandTests に "OccupySize" の undo/redo テストはあるが、あれは ImageCopy
    // (コピー層 = 新規配置の初期値) の OccupySize であり、配置層の OccupySize ではない
    // (BOM の D-1 / audit_focus「OccupySize 二層」の取り違え)。表面的なカバレッジ走査では
    // 「OccupySize undo テストはある」と誤認するが、GRID_COMPOSITION 自身の不変条件は無防備だった。
    [Fact]
    public async Task UpdatePlacementOccupySizeCommand_RoundTrip()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);   // 配置の初期 OccupySize は 1×1 を継承
        var placement = (await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0))).Value;

        // 前提を明示: 配置は copy から 1×1 を継承している (fixture 既定が変わったら気づけるように)。
        placement.OccupySize.Should().Be(OccupySize.OneByOne);

        var before = OccupySize.OneByOne;        // 1×1
        var after = new OccupySize(2, 2);        // 拡張 (3×3 グリッドの (0,0) に収まり、他配置なし → 検証 OK)
        var command = new UpdatePlacementOccupySizeCommand(_occupy, grid.Id, placement.Id,
            before: before, after: after,
            description: "占有セル: テスト");

        // Execute → 拡張 (AR-04: 拡張は境界+重複を検証)
        await _history.ExecuteAsync(command);
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.OccupySize.Should().Be(after);

        // Undo → 縮小して before に戻る (AR-07 before/after 対称。Undo が after を再適用する退行を捕捉)
        await _history.UndoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.OccupySize.Should().Be(before);

        // Redo → 再拡張
        await _history.RedoAsync();
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id))!.OccupySize.Should().Be(after);
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
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        if (added.IsError)
            throw new InvalidOperationException("Seed grid failed");
        return added.Value;
    }
}
