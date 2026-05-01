using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;
using ViewGrid.Infrastructure.Services;
using Xunit;

namespace ViewGrid.Application.Tests.ViewModels;

/// <summary>
/// MainWindowViewModel のナビゲーション機能（<see cref="NavigateToCopyPropertiesMessage"/>
/// 受信時の挙動）を検証する。Inspector の「特性を編集 →」コマンドから飛ばすルートと、
/// 直接 NavigateAsync を呼ぶルートの両方を確認する。
/// </summary>
public sealed class MainWindowViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private WeakReferenceMessenger _messenger = null!;
    private AssetLibraryViewModel _assetLibrary = null!;
    private CopyListViewModel _copyList = null!;
    private CopyPropertiesViewModel _copyProperties = null!;
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
            logger: NullLogger<ImportImageUseCase>.Instance);
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var sharedHistory = new ViewGrid.Application.History.UndoRedoService();
        _assetLibrary = new AssetLibraryViewModel(
            import, deleteAsset, _fx.AssetRepository, _fx.Thumbnails, picker, _messenger, sharedHistory,
            NullLogger<AssetLibraryViewModel>.Instance);

        // CopyListViewModel
        var createCopy = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
        var updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository);
        _copyList = new CopyListViewModel(
            _fx.CopyRepository, _fx.AssetRepository, _fx.Thumbnails, _fx.Storage,
            createCopy, updateCopy, _messenger, sharedHistory,
            NullLogger<CopyListViewModel>.Instance);

        // CopyPropertiesViewModel
        _copyProperties = new CopyPropertiesViewModel(
            updateCopy, sharedHistory, _messenger, _fx.ColorPicker, _fx.AutoCropResolver,
            NullLogger<CopyPropertiesViewModel>.Instance);

        // GridCanvasListViewModel
        var createGrid = new CreateGridCanvasUseCase(_fx.GridRepository);
        var deleteGrid = new DeleteGridCanvasUseCase(_fx.GridRepository);
        var renameGrid = new RenameGridCanvasUseCase(_fx.GridRepository);
        var setActive = new SetActiveGridCanvasUseCase(_fx.GridRepository);
        _gridList = new GridCanvasListViewModel(
            _fx.GridRepository, createGrid, deleteGrid, renameGrid, setActive, sharedHistory,
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
        var inspector = new PlacementInspectorViewModel(
            offset, _fx.PlacementRepository, sharedHistory, _messenger,
            NullLogger<PlacementInspectorViewModel>.Instance);
        var updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(_fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository,
            _fx.CropResolver, updateWeights,
            NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        _gridWorkspace = new GridWorkspaceViewModel(
            _fx.GridRepository, _fx.CopyRepository, _fx.AssetRepository, _fx.PlacementRepository,
            _fx.Thumbnails, _fx.Storage, _fx.CropResolver,
            place, remove, move, swap, render, export, updateWeights, updateLocks, offset,
            fitWeight, picker, _messenger, sharedHistory, inspector,
            NullLogger<GridWorkspaceViewModel>.Instance);

        _vm = new MainWindowViewModel(
            _assetLibrary, _copyList, _copyProperties, _gridList, _gridWorkspace, _messenger, sharedHistory);
    }

    public async Task DisposeAsync()
    {
        _vm.Dispose();
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task NavigateAsync_Switches_To_Preparation_Tab_And_Selects_Asset_And_Copy()
    {
        // Arrange: アセットとコピーをシードして、AssetLibrary を読み込み
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        await _assetLibrary.LoadAsync();
        // 配置タブから始まる想定
        _vm.SelectedTabIndex = MainWindowViewModel.LayoutTabIndex;

        // Act
        await _vm.NavigateAsync(asset.Id, copy.Id);

        // Assert
        _vm.SelectedTabIndex.Should().Be(MainWindowViewModel.PreparationTabIndex);
        _assetLibrary.SelectedAsset.Should().NotBeNull();
        _assetLibrary.SelectedAsset!.AssetId.Should().Be(asset.Id);
        _copyList.SelectedCopy.Should().NotBeNull();
        _copyList.SelectedCopy!.CopyId.Should().Be(copy.Id);
        _copyList.SelectedCopies.Should().ContainSingle(c => c.CopyId == copy.Id);
    }

    [Fact]
    public async Task NavigateAsync_With_Unknown_Asset_Does_Not_Throw_And_Still_Switches_Tab()
    {
        // 存在しない AssetId を渡しても例外を投げず、タブだけ切り替わる
        await _assetLibrary.LoadAsync();
        _vm.SelectedTabIndex = MainWindowViewModel.LayoutTabIndex;

        await _vm.NavigateAsync(System.Guid.NewGuid(), System.Guid.NewGuid());

        _vm.SelectedTabIndex.Should().Be(MainWindowViewModel.PreparationTabIndex);
        _assetLibrary.SelectedAsset.Should().BeNull();
    }

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
            IsActive = true,
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

    /// <summary>
    /// Copy 特性の Undo を MainWindow 経由で実行すると、CopyList の VM も DB と同期される。
    /// CopyPropertiesViewModel.SaveAsync が _source（CopyItemViewModel）に書いた値が
    /// Undo の DB ロールバックと乖離する問題を解消する。
    /// CopyName は特性タブの編集対象から外したため、Rotation を編集対象として round-trip を検証する。
    /// </summary>
    [Fact]
    public async Task UndoAsync_Reloads_CopyList_After_Property_Edit()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "name");

        await _assetLibrary.LoadAsync();
        _assetLibrary.SelectedAsset = _assetLibrary.Assets.Single();
        // OnAssetLibraryPropertyChanged の自動ロード完了を待つ
        for (var i = 0; i < 20 && _copyList.Copies.Count == 0; i++)
            await Task.Delay(50);
        _copyList.Copies.Should().ContainSingle();
        var copyItem = _copyList.Copies[0];
        _copyList.SelectedCopy = copyItem;
        copyItem.Rotation.Should().Be(Rotation.None);

        // Save 経由で Rotation を更新（特性タブの編集対象）
        _copyProperties.Rotation = Rotation.Cw90;
        await _copyProperties.SaveAsync();
        copyItem.Rotation.Should().Be(Rotation.Cw90);

        // Ctrl+Z で Undo → CopyList の VM も None に戻る（DB ロールバックと同期）
        await _vm.UndoAsync();
        for (var i = 0; i < 20 && _copyList.Copies.FirstOrDefault()?.Rotation != Rotation.None; i++)
            await Task.Delay(50);
        _copyList.Copies[0].Rotation.Should().Be(Rotation.None);
    }

    /// <summary>
    /// 先頭以外のコピーを選択して特性編集 → Save → Undo したとき、
    /// CopyList の SelectedCopy が「編集していたコピー」のまま維持されること（先頭にジャンプしない）。
    /// LoadForAssetAsync は SelectedCopy を Copies.FirstOrDefault() に強制リセットするため、
    /// MainWindowViewModel.RefreshAfterHistoryAsync で復元処理を入れている。
    /// </summary>
    [Fact]
    public async Task UndoAsync_Preserves_SelectedCopy_After_Refresh()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy0 = await _fx.SeedCopyAsync(asset.Id, copyName: "first");
        var copy1 = await _fx.SeedCopyAsync(asset.Id, copyName: "second");

        await _assetLibrary.LoadAsync();
        _assetLibrary.SelectedAsset = _assetLibrary.Assets.Single();
        // 自動ロードを待つ
        for (var i = 0; i < 20 && _copyList.Copies.Count < 2; i++)
            await Task.Delay(50);
        _copyList.Copies.Should().HaveCount(2);

        // 先頭ではない 2 番目のコピーを選択して編集（CopyName は特性タブ対象外なので Rotation で代替）
        var second = _copyList.Copies.Single(c => c.CopyId == copy1.Id);
        _copyList.SelectedCopy = second;
        _copyProperties.Rotation = Rotation.Cw180;
        await _copyProperties.SaveAsync();

        // Undo → SelectedCopy は second のまま、Rotation は元に戻る
        await _vm.UndoAsync();
        for (var i = 0; i < 20 && _copyList.Copies.FirstOrDefault(c => c.CopyId == copy1.Id)?.Rotation != Rotation.None; i++)
            await Task.Delay(50);

        _copyList.SelectedCopy.Should().NotBeNull();
        _copyList.SelectedCopy!.CopyId.Should().Be(copy1.Id); // 先頭（copy0）にジャンプしない
        _copyList.SelectedCopy.Rotation.Should().Be(Rotation.None);
    }

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
            IsActive = true,
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
            IsActive = true,
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
            IsActive = true,
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
            IsActive = true,
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
            IsActive = true,
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
            IsActive = true,
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

    [Fact]
    public async Task Send_NavigateMessage_Triggers_Tab_Switch_And_Selection()
    {
        // PlacementInspector の「特性を編集 →」が送る経路。
        // Receive は async void なので、ハンドラ完了を待つために少し sleep する。
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        await _assetLibrary.LoadAsync();
        _vm.SelectedTabIndex = MainWindowViewModel.LayoutTabIndex;

        _messenger.Send(new NavigateToCopyPropertiesMessage(asset.Id, copy.Id));

        // Receive が fire-and-forget なので、最大 1 秒待ってアサート
        for (var i = 0; i < 20 && _copyList.SelectedCopy?.CopyId != copy.Id; i++)
            await Task.Delay(50);

        _vm.SelectedTabIndex.Should().Be(MainWindowViewModel.PreparationTabIndex);
        _copyList.SelectedCopy.Should().NotBeNull();
        _copyList.SelectedCopy!.CopyId.Should().Be(copy.Id);
    }
}
