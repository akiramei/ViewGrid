using System.Collections.Immutable;
using FluentAssertions;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.History.Commands;

/// <summary>
/// 特性/グリッド系 4 Command（UpdateImageCopy / UpdateGridWeights / UpdateGridLocks /
/// RenameGridCanvas）の Execute → Undo → Redo round-trip 検証。
/// </summary>
public sealed class GridAndCopyCommandTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UndoRedoService _history = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _history = new UndoRedoService();
    }

    public async Task DisposeAsync()
    {
        _history.Dispose();
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task UpdateImageCopyCommand_RoundTrip_Restores_All_Fields()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, "before");
        var useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        var before = UpdateImageCopyCommand.SnapshotFrom(copy);
        var after = new UpdateImageCopyChanges
        {
            CopyName = "after",
            Transform = new ImageTransform(Rotation.Cw90, FlipX: true, FlipY: false),
            ScalingMode = ScalingMode.UniformCover,
            Alignment = new Alignment(AnchorX.Right, AnchorY.Bottom),
            OccupySize = new OccupySize(2, 1),
        };

        var command = new UpdateImageCopyCommand(useCase, copy.Id, before, after,
            description: "特性編集: テスト");

        await _history.ExecuteAsync(command);
        var afterExec = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        afterExec!.CopyName.Should().Be("after");
        afterExec.Transform.Rotation.Should().Be(Rotation.Cw90);
        afterExec.ScalingMode.Should().Be(ScalingMode.UniformCover);
        afterExec.OccupySize.Should().Be(new OccupySize(2, 1));

        await _history.UndoAsync();
        var afterUndo = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        afterUndo!.CopyName.Should().Be("before");
        afterUndo.Transform.Rotation.Should().Be(Rotation.None);
        afterUndo.ScalingMode.Should().Be(ScalingMode.UniformContain);
        afterUndo.OccupySize.Should().Be(OccupySize.OneByOne);

        await _history.RedoAsync();
        var afterRedo = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        afterRedo!.CopyName.Should().Be("after");
        afterRedo.OccupySize.Should().Be(new OccupySize(2, 1));
    }

    [Fact]
    public async Task UpdateImageCopyCommand_RoundTrip_Restores_Regions()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        // 初期状態: Region 1 件 (Undo で戻る対象)。 SeedCopyAsync は Regions を持たないので、
        // 「初期 Region あり」 の状態を最初に作るため UseCase 経由で 1 件追加してから snapshot を取る。
        var initialRegion = BuildRegion(copy.Id, 0, new RegionRectFraction(0.1, 0.1, 0.2, 0.2));
        await useCase.ExecuteAsync(copy.Id, new UpdateImageCopyChanges
        {
            Regions = ImmutableArray.Create(initialRegion),
        });
        var copyWithInitialRegion = await _fx.CopyRepository.FindByIdAsync(copy.Id);

        var before = UpdateImageCopyCommand.SnapshotFrom(copyWithInitialRegion!);
        // after: 元 Region を残しつつ別 Region を 1 件追加 (合計 2 件)
        var addedRegion = BuildRegion(copy.Id, 1, new RegionRectFraction(0.5, 0.5, 0.3, 0.3));
        var after = new UpdateImageCopyChanges
        {
            Regions = ImmutableArray.Create(initialRegion, addedRegion),
        };

        var command = new UpdateImageCopyCommand(useCase, copy.Id, before, after,
            description: "保護領域追加: テスト");

        await _history.ExecuteAsync(command);
        var afterExec = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        afterExec!.Regions.Should().HaveCount(2);
        afterExec.Regions.Should().Contain(r => r.Id == initialRegion.Id);
        afterExec.Regions.Should().Contain(r => r.Id == addedRegion.Id);

        await _history.UndoAsync();
        var afterUndo = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        afterUndo!.Regions.Should().HaveCount(1);
        afterUndo.Regions[0].Id.Should().Be(initialRegion.Id);

        await _history.RedoAsync();
        var afterRedo = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        afterRedo!.Regions.Should().HaveCount(2);
        afterRedo.Regions.Should().Contain(r => r.Id == addedRegion.Id);
    }

    [Fact]
    public async Task UpdateImageCopyCommand_SnapshotFrom_Captures_Empty_Regions_For_Undo_To_Empty()
    {
        // Undo で 「Region なし」 状態に戻すには、 before snapshot の Regions が
        // ImmutableArray.Empty (null ではない) として保存されている必要がある。
        // これが null だと UseCase が 「変更なし」 と解釈してしまい、 Undo が機能しない。
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        // 初期状態は Region なし
        var before = UpdateImageCopyCommand.SnapshotFrom(copy);
        before.Regions.Should().NotBeNull();
        before.Regions!.Value.Should().BeEmpty();

        // after: Region 1 件追加
        var addedRegion = BuildRegion(copy.Id, 0, new RegionRectFraction(0.0, 0.0, 0.5, 0.5));
        var after = new UpdateImageCopyChanges
        {
            Regions = ImmutableArray.Create(addedRegion),
        };
        var command = new UpdateImageCopyCommand(useCase, copy.Id, before, after,
            description: "初回保護領域追加: テスト");

        await _history.ExecuteAsync(command);
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.Regions.Should().HaveCount(1);

        // Undo: Region なしの状態に戻る
        await _history.UndoAsync();
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.Regions.Should().BeEmpty();
    }

    private static ProtectedRegion BuildRegion(Guid copyId, int sortOrder, RegionRectFraction rect) => new()
    {
        Id = Guid.NewGuid(),
        ImageCopyId = copyId,
        Rect = rect,
        FillMode = ProtectedRegionFillMode.White,
        SortOrder = sortOrder,
    };

    [Fact]
    public async Task UpdateGridWeightsCommand_RoundTrip()
    {
        var grid = await SeedGridAsync(rows: 3, cols: 3);
        var useCase = new UpdateGridWeightsUseCase(_fx.GridRepository);

        var beforeCol = grid.ColWeights;
        var beforeRow = grid.RowWeights;
        var afterCol = ImmutableArray.Create(2, 1, 1);
        var afterRow = ImmutableArray.Create(1, 3, 1);

        var command = new UpdateGridWeightsCommand(useCase, grid.Id, beforeCol, beforeRow, afterCol, afterRow,
            description: "比率変更: テスト");

        await _history.ExecuteAsync(command);
        var execGrid = await _fx.GridRepository.FindByIdAsync(grid.Id);
        execGrid!.ColWeights.Should().Equal(afterCol);
        execGrid.RowWeights.Should().Equal(afterRow);

        await _history.UndoAsync();
        var undoGrid = await _fx.GridRepository.FindByIdAsync(grid.Id);
        undoGrid!.ColWeights.Should().Equal(beforeCol);
        undoGrid.RowWeights.Should().Equal(beforeRow);

        await _history.RedoAsync();
        var redoGrid = await _fx.GridRepository.FindByIdAsync(grid.Id);
        redoGrid!.ColWeights.Should().Equal(afterCol);
    }

    [Fact]
    public async Task UpdateGridLocksCommand_RoundTrip()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);
        var useCase = new UpdateGridLocksUseCase(_fx.GridRepository);

        var beforeCol = grid.ColLocked.Length == grid.GridCols
            ? grid.ColLocked
            : [.. Enumerable.Range(0, grid.GridCols).Select(_ => false)];
        var beforeRow = grid.RowLocked.Length == grid.GridRows
            ? grid.RowLocked
            : [.. Enumerable.Range(0, grid.GridRows).Select(_ => false)];
        var afterCol = ImmutableArray.Create(true, false, true);
        var afterRow = ImmutableArray.Create(false, true);

        var command = new UpdateGridLocksCommand(useCase, grid.Id, beforeCol, beforeRow, afterCol, afterRow,
            description: "ロック切替: テスト");

        await _history.ExecuteAsync(command);
        var execGrid = await _fx.GridRepository.FindByIdAsync(grid.Id);
        execGrid!.ColLocked.Should().Equal(afterCol);
        execGrid.RowLocked.Should().Equal(afterRow);

        await _history.UndoAsync();
        var undoGrid = await _fx.GridRepository.FindByIdAsync(grid.Id);
        undoGrid!.ColLocked.Should().Equal(beforeCol);
        undoGrid.RowLocked.Should().Equal(beforeRow);

        await _history.RedoAsync();
        var redoGrid = await _fx.GridRepository.FindByIdAsync(grid.Id);
        redoGrid!.ColLocked.Should().Equal(afterCol);
    }

    [Fact]
    public async Task RenameGridCanvasCommand_RoundTrip()
    {
        var grid = await SeedGridAsync(name: "before");
        var useCase = new RenameGridCanvasUseCase(_fx.GridRepository);

        var command = new RenameGridCanvasCommand(useCase, grid.Id, "before", "after",
            description: "リネーム: 「before」→「after」");

        await _history.ExecuteAsync(command);
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.Name.Should().Be("after");

        await _history.UndoAsync();
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.Name.Should().Be("before");

        await _history.RedoAsync();
        (await _fx.GridRepository.FindByIdAsync(grid.Id))!.Name.Should().Be("after");
    }

    /// <summary>
    /// 元の CopyName が <c>null</c>（無名）→ 命名 → Undo で再び <c>null</c> に戻ることを保証する。
    /// <see cref="UpdateImageCopyChanges.ClearCopyName"/> フラグなしだと <c>null</c> が
    /// 「変更しない」と解釈されて Undo が効かない（旧バグの回帰防止）。
    /// </summary>
    [Fact]
    public async Task UpdateImageCopyCommand_Undo_Restores_Null_CopyName()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: null); // 元は無名
        var useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        // before（無名状態）→ after（"new name"）
        var before = UpdateImageCopyCommand.SnapshotFrom(copy); // ClearCopyName=true のはず
        var after = before with { CopyName = "new name", ClearCopyName = false };
        var command = new UpdateImageCopyCommand(useCase, copy.Id, before, after,
            description: "特性編集: 「(無名)」→「new name」");

        await _history.ExecuteAsync(command);
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.CopyName.Should().Be("new name");

        await _history.UndoAsync();
        // Undo で「無名」に戻る。null が「変更しない」と誤解釈されるとここで "new name" が残る。
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.CopyName.Should().BeNull();

        await _history.RedoAsync();
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.CopyName.Should().Be("new name");
    }

    /// <summary>
    /// 命名 → 名前消去（空文字 → null 保存）→ Undo で名前が戻ることを保証する。
    /// </summary>
    [Fact]
    public async Task UpdateImageCopyCommand_Undo_Restores_Cleared_CopyName()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        var before = UpdateImageCopyCommand.SnapshotFrom(copy); // ClearCopyName=false
        // 名前を消す（after は明示的 null + ClearCopyName=true）
        var after = before with { CopyName = null, ClearCopyName = true };
        var command = new UpdateImageCopyCommand(useCase, copy.Id, before, after,
            description: "特性編集: 「original」→「(無名)」");

        await _history.ExecuteAsync(command);
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.CopyName.Should().BeNull();

        await _history.UndoAsync();
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.CopyName.Should().Be("original");

        await _history.RedoAsync();
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.CopyName.Should().BeNull();
    }

    [Fact]
    public async Task UpdateImageCopyCommand_AffectedGridId_Is_Null()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var useCase = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        var before = UpdateImageCopyCommand.SnapshotFrom(copy);
        var command = new UpdateImageCopyCommand(useCase, copy.Id, before, before,
            description: "特性編集: テスト");

        // 共有コピーは特定のグリッドに紐付かない
        command.AffectedGridId.Should().BeNull();
        command.Description.Should().Be("特性編集: テスト");
    }

    private async Task<GridCanvas> SeedGridAsync(int rows = 3, int cols = 3, string name = "test")
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = name,
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
