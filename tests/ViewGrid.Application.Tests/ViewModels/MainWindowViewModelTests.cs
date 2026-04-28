using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
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
        _assetLibrary = new AssetLibraryViewModel(
            import, deleteAsset, _fx.AssetRepository, _fx.Thumbnails, picker, _messenger,
            NullLogger<AssetLibraryViewModel>.Instance);

        // CopyListViewModel
        var createCopy = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
        _copyList = new CopyListViewModel(
            _fx.CopyRepository, createCopy, _messenger,
            NullLogger<CopyListViewModel>.Instance);

        // CopyPropertiesViewModel
        var updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository);
        _copyProperties = new CopyPropertiesViewModel(
            updateCopy, _messenger, NullLogger<CopyPropertiesViewModel>.Instance);

        // GridCanvasListViewModel
        var createGrid = new CreateGridCanvasUseCase(_fx.GridRepository);
        var deleteGrid = new DeleteGridCanvasUseCase(_fx.GridRepository);
        var renameGrid = new RenameGridCanvasUseCase(_fx.GridRepository);
        var setActive = new SetActiveGridCanvasUseCase(_fx.GridRepository);
        _gridList = new GridCanvasListViewModel(
            _fx.GridRepository, createGrid, deleteGrid, renameGrid, setActive,
            NullLogger<GridCanvasListViewModel>.Instance);

        // GridWorkspaceViewModel + PlacementInspector
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        var move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var swap = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var render = new RenderGridUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository,
            _fx.AssetRepository, _fx.Storage, new SkiaGridImageRenderer());
        var export = new ExportGridUseCase(render);
        var offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        var inspector = new PlacementInspectorViewModel(
            offset, _messenger, NullLogger<PlacementInspectorViewModel>.Instance);
        var updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(_fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository, updateWeights,
            NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        _gridWorkspace = new GridWorkspaceViewModel(
            _fx.GridRepository, _fx.CopyRepository, _fx.AssetRepository, _fx.PlacementRepository,
            _fx.Thumbnails, place, remove, move, swap, render, export, updateWeights, updateLocks, offset,
            fitWeight, picker, _messenger, inspector,
            NullLogger<GridWorkspaceViewModel>.Instance);

        _vm = new MainWindowViewModel(
            _assetLibrary, _copyList, _copyProperties, _gridList, _gridWorkspace, _messenger);
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
