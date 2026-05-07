using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.ViewModels;

/// <summary>
/// MainWindowViewModel の Undo/Redo + ステータス / ヒント / 未保存バッジを検証する。
/// 配置ファースト UI 第 2 段階 Stage 4 で準備タブが廃止されたため、
/// SelectedTabIndex / NavigateAsync / NavigateToCopyPropertiesMessage 関連のテストは撤去された。
/// </summary>
public sealed class MainWindowViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private WeakReferenceMessenger _messenger = null!;
    private AssetLibraryViewModel _assetLibrary = null!;
    private GridCanvasListViewModel _gridList = null!;
    private GridWorkspaceViewModel _gridWorkspace = null!;
    private MainWindowViewModel _vm = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _messenger = new WeakReferenceMessenger();
        var picker = Substitute.For<IFilePickerService>();

        // AssetLibraryViewModel
        var import = new ImportImageUseCase(
            hasher: new Sha256ImageHasher(),
            prober: new SkiaImageProber(),
            storage: _fx.Storage,
            thumbnailService: _fx.Thumbnails,
            assetRepository: _fx.AssetRepository,
            copyRepository: _fx.CopyRepository,
            settings: _fx.AppSettings,
            logger: NullLogger<ImportImageUseCase>.Instance);
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var sharedHistory = new ViewGrid.Application.History.UndoRedoService();
        _assetLibrary = new AssetLibraryViewModel(
            import, deleteAsset, _fx.AssetRepository, _fx.Thumbnails, picker, _messenger, sharedHistory,
            NullLogger<AssetLibraryViewModel>.Instance);

        var createCopy = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
        var updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);

        // CopyPropertiesViewModel: PlacementInspector に inline embed されるため必要
        var copyProperties = new CopyPropertiesViewModel(
            updateCopy, sharedHistory, _messenger, _fx.ColorPicker, _fx.AutoCropResolver, _fx.AppSettings,
            NullLogger<CopyPropertiesViewModel>.Instance);

        // GridCanvasListViewModel
        var createGrid = new CreateGridCanvasUseCase(_fx.GridRepository);
        var deleteGrid = new DeleteGridCanvasUseCase(_fx.GridRepository);
        var renameGrid = new RenameGridCanvasUseCase(_fx.GridRepository);
        var updateGridSize = new UpdateGridCanvasSizeUseCase(_fx.GridRepository);
        _gridList = new GridCanvasListViewModel(
            _fx.GridRepository, createGrid, deleteGrid, renameGrid, updateGridSize, _fx.AppSettings, sharedHistory,
            NullLogger<GridCanvasListViewModel>.Instance);

        // GridWorkspaceViewModel + PlacementInspector
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        var move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var swap = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var render = new RenderGridUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository,
            _fx.AssetRepository, _fx.Storage, new SkiaGridImageRenderer(new AutoCropCache()));
        var export = new ExportGridUseCase(render);
        var offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        var occupy = new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository);
        var fork = new ForkPlacementVariantUseCase(_fx.CopyRepository, _fx.PlacementRepository);
        var inspector = new PlacementInspectorViewModel(
            offset, occupy, fork, _fx.PlacementRepository, _fx.CopyRepository,
            _fx.AssetRepository, _fx.Thumbnails, _fx.Storage,
            copyProperties, sharedHistory, _messenger, _fx.AppSettings,
            NullLogger<PlacementInspectorViewModel>.Instance);
        var updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(_fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository,
            _fx.CropResolver, updateWeights,
            NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        _gridWorkspace = new GridWorkspaceViewModel(
            _fx.GridRepository, _fx.CopyRepository, _fx.AssetRepository, _fx.PlacementRepository,
            _fx.Thumbnails, _fx.CropResolver,
            place, remove, move, swap, render, export, updateWeights, updateLocks, offset,
            fitWeight, createCopy, updateCopy, picker, _messenger, sharedHistory, inspector,
            NullLogger<GridWorkspaceViewModel>.Instance);

        _vm = new MainWindowViewModel(
            _assetLibrary, _gridList, _gridWorkspace, _messenger, sharedHistory);
    }

    public async Task DisposeAsync()
    {
        _vm.Dispose();
        await _fx.DisposeAsync();
    }

    // Stage 4 で NavigateAsync / NavigateToCopyPropertiesMessage は撤去された。
    // 共有特性の編集は配置タブ Inspector の inline embed で完結する。

    /// <summary>
    /// グリッドリネームを Undo すると、サイドバー（GridList.Grids）にも旧名が反映されることを確認。
    /// 過去には MainWindowViewModel.UndoAsync が CopyLibraryChangedMessage しか送らず、
    /// GridCanvasListViewModel は受信していないためサイドバーが古い名前を保持する不具合があった。
    /// </summary>
    [Fact]
    public async Task UndoAsync_Reloads_GridList_After_Rename()
    {
        // Seed: 名前 "old" のグリッドを 1 つ
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "old",
            GridRows = 2,
            GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        (await _fx.GridRepository.AddAsync(grid)).IsError.Should().BeFalse();

        await _gridList.LoadAsync();
        _gridList.Grids.Should().ContainSingle();
        _gridList.SelectedGrid.Should().NotBeNull();

        // Rename → "new" via Command path
        await _gridList.RenameSelectedAsync("new");
        _gridList.Grids[0].Name.Should().Be("new");

        // Ctrl+Z → DB rolls back AND sidebar should reflect "old" again
        await _vm.UndoAsync();

        _gridList.Grids.Should().ContainSingle();
        _gridList.Grids[0].Name.Should().Be("old");
    }

    // Stage 4 で「準備タブで Asset 選択 → CopyList 自動ロード → Undo で再同期」の経路は撤去された。
    // CopyList ベースの Undo 整合性テストは廃止し、Inspector / Placement 経路の Undo round-trip
    // 検証は PlacementInspectorViewModelTests / GridWorkspaceViewModelTests に集約されている。

    /// <summary>
    /// MainWindowViewModel.HistoryEntries が IUndoRedoService.History を反映する。
    /// </summary>
    [Fact]
    public async Task HistoryEntries_Reflects_Service_State()
    {
        _vm.HistoryEntries.Should().BeEmpty();
        _vm.CurrentHistoryIndex.Should().Be(-1);
        _vm.HasHistory.Should().BeFalse();

        // 配置 1 件の操作を行うと履歴に積まれる
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "test",
            GridRows = 2,
            GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        await _gridList.LoadAsync();
        await _gridList.RenameSelectedAsync("renamed");

        _vm.HistoryEntries.Should().ContainSingle();
        _vm.HistoryEntries[0].Description.Should().Contain("リネーム");
        _vm.CurrentHistoryIndex.Should().Be(0);
        _vm.HasHistory.Should().BeTrue();
    }

    /// <summary>
    /// JumpToHistoryAsync が複数ステップの一括 Undo / Redo を行い、各 VM が再ロードされる。
    /// </summary>
    [Fact]
    public async Task JumpToHistoryAsync_Reverts_To_Earlier_State()
    {
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "step0",
            GridRows = 2,
            GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        await _gridList.LoadAsync();

        await _gridList.RenameSelectedAsync("step1");
        await _gridList.RenameSelectedAsync("step2");
        await _gridList.RenameSelectedAsync("step3");

        _vm.CurrentHistoryIndex.Should().Be(2);
        _gridList.Grids[0].Name.Should().Be("step3");

        // Index=0（step1 適用済みの状態）にジャンプ
        await _vm.JumpToHistoryAsync(0);

        _vm.CurrentHistoryIndex.Should().Be(0);
        _gridList.Grids[0].Name.Should().Be("step1");
    }

    /// <summary>
    /// JumpToHistoryAsync で範囲外の Index を指定しても例外は出ない（Service 側で Validation エラー）。
    /// </summary>
    [Fact]
    public async Task JumpToHistoryAsync_OutOfRange_Returns_Without_Throwing()
    {
        // 履歴空の状態で範囲外ジャンプ
        await _vm.Awaiting(v => v.JumpToHistoryAsync(99)).Should().NotThrowAsync();
        await _vm.Awaiting(v => v.JumpToHistoryAsync(-99)).Should().NotThrowAsync();
        _vm.HistoryEntries.Should().BeEmpty();
    }

    [Fact]
    public void HoveredHistoryIndex_Null_Has_No_Range()
    {
        _vm.HoveredHistoryIndex = null;

        _vm.HoveredJumpRangeLo.Should().Be(-1);
        _vm.HoveredJumpRangeHi.Should().Be(-1);
        _vm.HoveredJumpDirection.Should().Be(ViewGrid.Application.History.JumpDirection.None);
    }

    [Fact]
    public async Task HoveredHistoryIndex_Newer_Than_Current_Sets_Redo_Range()
    {
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "g",
            GridRows = 2, GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        await _gridList.LoadAsync();
        await _gridList.RenameSelectedAsync("a");
        await _gridList.RenameSelectedAsync("b");
        await _gridList.RenameSelectedAsync("c");
        await _vm.UndoAsync(); // CurrentIndex = 1, Index 2 は redo 候補

        _vm.HoveredHistoryIndex = 2;

        _vm.HoveredJumpRangeLo.Should().Be(2);
        _vm.HoveredJumpRangeHi.Should().Be(2);
        _vm.HoveredJumpDirection.Should().Be(ViewGrid.Application.History.JumpDirection.Redo);
    }

    [Fact]
    public async Task HoveredHistoryIndex_Older_Than_Current_Sets_Undo_Range()
    {
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "g",
            GridRows = 2, GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        await _gridList.LoadAsync();
        await _gridList.RenameSelectedAsync("a");
        await _gridList.RenameSelectedAsync("b");
        await _gridList.RenameSelectedAsync("c");
        // CurrentIndex = 2

        _vm.HoveredHistoryIndex = 0;

        // [hover+1, current] = [1, 2] が Undo 範囲
        _vm.HoveredJumpRangeLo.Should().Be(1);
        _vm.HoveredJumpRangeHi.Should().Be(2);
        _vm.HoveredJumpDirection.Should().Be(ViewGrid.Application.History.JumpDirection.Undo);
    }

    [Fact]
    public async Task HoveredHistoryIndex_Same_As_Current_Has_No_Range()
    {
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "g",
            GridRows = 2, GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        await _gridList.LoadAsync();
        await _gridList.RenameSelectedAsync("a");

        _vm.HoveredHistoryIndex = _vm.CurrentHistoryIndex;

        _vm.HoveredJumpRangeLo.Should().Be(-1);
        _vm.HoveredJumpRangeHi.Should().Be(-1);
        _vm.HoveredJumpDirection.Should().Be(ViewGrid.Application.History.JumpDirection.None);
    }

    [Fact]
    public async Task Undo_Recalculates_Hover_Range_When_Hover_Active()
    {
        var grid = new ViewGrid.Core.Entities.GridCanvas
        {
            Id = System.Guid.NewGuid(),
            Name = "g",
            GridRows = 2, GridCols = 2,
            ColWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            RowWeights = ViewGrid.Core.Entities.GridCanvas.UniformWeights(2),
            CanvasSize = new ViewGrid.Core.Entities.PixelSize(400, 400),
            CreatedAt = System.DateTimeOffset.UtcNow,
            UpdatedAt = System.DateTimeOffset.UtcNow,
        };
        await _fx.GridRepository.AddAsync(grid);
        await _gridList.LoadAsync();
        await _gridList.RenameSelectedAsync("a");
        await _gridList.RenameSelectedAsync("b");
        // CurrentIndex = 1

        _vm.HoveredHistoryIndex = 0; // Undo 方向、範囲 [1, 1]
        _vm.HoveredJumpDirection.Should().Be(ViewGrid.Application.History.JumpDirection.Undo);

        // Undo を実行すると CurrentIndex = 0 になり、hover 0 と一致 → None になる
        await _vm.UndoAsync();

        _vm.HoveredJumpDirection.Should().Be(ViewGrid.Application.History.JumpDirection.None);
    }

    /// <summary>
    /// アセット 0 件のときは「ドラッグ&ドロップで取り込み」案内が出る（Stage 4 後はタブに依存しない）。
    /// </summary>
    [Fact]
    public void CurrentHints_Empty_Library_Shows_Drop_Hint()
    {
        _vm.CurrentHints.Should().Contain("ドラッグ");
    }

    /// <summary>
    /// アセットがありグリッドが未選択なら、グリッド作成案内が出る。
    /// </summary>
    [Fact]
    public async Task CurrentHints_With_Assets_But_No_Grid_Shows_Create_Hint()
    {
        await _fx.SeedAssetAsync();
        await _assetLibrary.LoadAsync();
        _vm.CurrentHints.Should().Contain("グリッド");
    }

    /// <summary>
    /// Inspector.IsAnyDirty が立つと未保存バッジが表示される。
    /// Stage 3 で Inspector 統合の IsAnyDirty (placement + shared) を一本化、
    /// Stage 4 で MainWindow はこれだけを未保存集約に使う。
    /// </summary>
    [Fact]
    public void UnsavedSummary_Reflects_Inspector_IsAnyDirty()
    {
        _vm.HasUnsavedChanges.Should().BeFalse();
        _vm.UnsavedSummary.Should().BeEmpty();

        _gridWorkspace.Inspector.IsDirty = true;

        _vm.HasUnsavedChanges.Should().BeTrue();
        _vm.UnsavedSummary.Should().Contain("未保存");
    }

    /// <summary>
    /// 共有特性 (CopyProperties.IsDirty) が立っても Inspector.IsAnyDirty 経由でバッジに反映される。
    /// </summary>
    [Fact]
    public void UnsavedSummary_Reflects_CopyProperties_Through_Inspector()
    {
        _vm.HasUnsavedChanges.Should().BeFalse();

        _gridWorkspace.Inspector.CopyProperties.IsDirty = true;

        _vm.HasUnsavedChanges.Should().BeTrue();
        _vm.UnsavedSummary.Should().Contain("未保存");
    }

    /// <summary>
    /// IsDirty が false に戻ると未保存バッジは消える（保存ボタン押下後の動きを想定）。
    /// </summary>
    [Fact]
    public void UnsavedSummary_Cleared_When_Dirty_Becomes_False()
    {
        _gridWorkspace.Inspector.IsDirty = true;
        _vm.HasUnsavedChanges.Should().BeTrue();

        _gridWorkspace.Inspector.IsDirty = false;

        _vm.HasUnsavedChanges.Should().BeFalse();
        _vm.UnsavedSummary.Should().BeEmpty();
    }
}
