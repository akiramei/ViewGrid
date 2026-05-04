using FluentAssertions;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.History.Commands;

/// <summary>
/// <see cref="ForkPlacementVariantCommand"/> の Execute → Undo → Redo round-trip を実 EF Core
/// in-memory SQLite で検証する。Undo は新規バリアント削除 + Placement.CopyId 復元、
/// Redo は同 Id で再 INSERT + 再付け替えを行う。
/// </summary>
public sealed class ForkPlacementVariantCommandTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private ForkPlacementVariantUseCase _fork = null!;
    private UndoRedoService _history = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _fork = new ForkPlacementVariantUseCase(_fx.CopyRepository, _fx.PlacementRepository);
        _history = new UndoRedoService();
    }

    public async Task DisposeAsync()
    {
        _history.Dispose();
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task Fork_Execute_Undo_Redo_RoundTrip()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var sourceCopy = await _fx.SeedCopyAsync(asset.Id, copyName: "オリジナル");
        var placement = await SeedPlacementAsync(grid.Id, sourceCopy.Id);

        var command = new ForkPlacementVariantCommand(
            _fork, _fx.CopyRepository, _fx.PlacementRepository,
            placement.Id, grid.Id,
            description: "バリアントを分岐: 「オリジナル」 → 派生");

        // Execute: 新規バリアント作成 + Placement.CopyId 付け替え
        var execResult = await _history.ExecuteAsync(command);
        execResult.IsError.Should().BeFalse();
        command.CreatedCopyId.Should().NotBeNull();
        command.OriginalCopyId.Should().Be(sourceCopy.Id);

        var newCopyId = command.CreatedCopyId!.Value;
        (await _fx.CopyRepository.FindByIdAsync(newCopyId)).Should().NotBeNull();

        var afterFork = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        afterFork!.CopyId.Should().Be(newCopyId);

        // Undo: 新規バリアント削除 + 元 CopyId 復元
        var undoResult = await _history.UndoAsync();
        undoResult.IsError.Should().BeFalse();
        (await _fx.CopyRepository.FindByIdAsync(newCopyId)).Should().BeNull();

        var afterUndo = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        afterUndo!.CopyId.Should().Be(sourceCopy.Id);

        // Redo: 同 Id で再 INSERT + 再付け替え
        var redoResult = await _history.RedoAsync();
        redoResult.IsError.Should().BeFalse();
        (await _fx.CopyRepository.FindByIdAsync(newCopyId)).Should().NotBeNull();

        var afterRedo = await _fx.PlacementRepository.FindByIdAsync(placement.Id);
        afterRedo!.CopyId.Should().Be(newCopyId);
    }

    [Fact]
    public async Task Fork_Does_Not_Affect_Other_Placements_Sharing_The_Variant()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var sourceCopy = await _fx.SeedCopyAsync(asset.Id, copyName: "共有");
        var p1 = await SeedPlacementAsync(grid.Id, sourceCopy.Id, position: new CellPosition(0, 0));
        var p2 = await SeedPlacementAsync(grid.Id, sourceCopy.Id, position: new CellPosition(1, 1));

        var command = new ForkPlacementVariantCommand(
            _fork, _fx.CopyRepository, _fx.PlacementRepository,
            p1.Id, grid.Id, description: "test");

        await _history.ExecuteAsync(command);

        // p1 だけ CopyId が変わり、p2 は元のまま
        var p1After = await _fx.PlacementRepository.FindByIdAsync(p1.Id);
        var p2After = await _fx.PlacementRepository.FindByIdAsync(p2.Id);
        p1After!.CopyId.Should().Be(command.CreatedCopyId!.Value);
        p2After!.CopyId.Should().Be(sourceCopy.Id);
    }

    [Fact]
    public async Task AffectedGridId_Is_Set_For_Auto_Switch()
    {
        var grid = await SeedGridAsync();
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var placement = await SeedPlacementAsync(grid.Id, copy.Id);

        var command = new ForkPlacementVariantCommand(
            _fork, _fx.CopyRepository, _fx.PlacementRepository,
            placement.Id, grid.Id, description: "test");

        command.AffectedGridId.Should().Be(grid.Id);
    }

    private async Task<GridCanvas> SeedGridAsync()
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
            GridRows = 2, GridCols = 2,
            ColWeights = GridCanvas.UniformWeights(2),
            RowWeights = GridCanvas.UniformWeights(2),
            CanvasSize = new PixelSize(400, 400),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        return added.Value;
    }

    private async Task<GridPlacement> SeedPlacementAsync(Guid gridId, Guid copyId, CellPosition? position = null)
    {
        var placement = new GridPlacement
        {
            Id = Guid.NewGuid(),
            GridId = gridId,
            CopyId = copyId,
            Position = position ?? new CellPosition(0, 0),
            OccupySize = OccupySize.OneByOne,
            PixelOffsetX = 0, PixelOffsetY = 0,
            PlacementOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.PlacementRepository.AddAsync(placement);
        return added.Value;
    }
}
