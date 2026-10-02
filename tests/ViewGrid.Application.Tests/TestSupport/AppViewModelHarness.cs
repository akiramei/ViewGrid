using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.History;
using ViewGrid.Application.Localization;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// アプリ本体と同じ組み合わせ (MainWindow / グリッド一覧 / ワークスペース / 各子 VM) を、 確認サービスだけ差し替えて
/// 組み立てるテスト用の土台。 確認ダイアログが「後から」 回答される状況 (<see cref="DeferredConfirmationService"/>) で
/// 状態遷移を試すために使う。
/// </summary>
internal sealed class AppViewModelHarness : IAsyncDisposable
{
    public required UseCaseFixture Fx { get; init; }
    public required WeakReferenceMessenger Messenger { get; init; }
    public required GateableUndoRedoService History { get; init; }
    public required GridCanvasListViewModel GridList { get; init; }
    public required GridWorkspaceViewModel Workspace { get; init; }
    public required MainWindowViewModel Main { get; init; }
    public required IConfirmationService Confirmation { get; init; }
    public required StubIsolatedRenderer Isolated { get; init; }

    public static async Task<AppViewModelHarness> CreateAsync(IConfirmationService confirmation)
    {
        var fx = await UseCaseFixture.CreateAsync();
        var messenger = new WeakReferenceMessenger();
        var history = new GateableUndoRedoService();
        var picker = Substitute.For<IFilePickerService>();
        var loc = new NullLocalizationService();

        var import = new ImportImageUseCase(
            new Sha256ImageHasher(), new SkiaImageProber(), fx.Storage, fx.Thumbnails,
            fx.AssetRepository, fx.CopyRepository, fx.AppSettings, NullLogger<ImportImageUseCase>.Instance);
        var deleteAsset = new DeleteImageAssetUseCase(fx.AssetRepository, fx.Storage, fx.Thumbnails);
        var assetLibrary = new AssetLibraryViewModel(
            import, deleteAsset, fx.AssetRepository, fx.Thumbnails, picker, messenger, history, loc,
            NullLogger<AssetLibraryViewModel>.Instance);

        var createCopy = new CreateLogicalCopyUseCase(fx.AssetRepository, fx.CopyRepository, fx.AppSettings);
        var updateCopy = new UpdateImageCopyUseCase(fx.CopyRepository, fx.PlacementRepository, fx.GridRepository);
        var gridList = new GridCanvasListViewModel(
            fx.GridRepository, new CreateGridCanvasUseCase(fx.GridRepository),
            new DeleteGridCanvasUseCase(fx.GridRepository), new RenameGridCanvasUseCase(fx.GridRepository),
            new UpdateGridCanvasSizeUseCase(fx.GridRepository), fx.PlacementRepository, confirmation,
            fx.AppSettings, history, loc, NullLogger<GridCanvasListViewModel>.Instance);

        var place = new PlaceImageCopyUseCase(fx.GridRepository, fx.CopyRepository, fx.PlacementRepository);
        var remove = new RemovePlacementUseCase(fx.PlacementRepository);
        var move = new MovePlacementUseCase(fx.GridRepository, fx.CopyRepository, fx.PlacementRepository);
        var swap = new SwapPlacementsUseCase(fx.GridRepository, fx.CopyRepository, fx.PlacementRepository);
        var render = new RenderGridUseCase(
            fx.GridRepository, fx.PlacementRepository, fx.CopyRepository, fx.AssetRepository, fx.Storage,
            new SkiaGridImageRenderer(new AutoCropCache()));
        var export = new ExportGridUseCase(render);
        var offset = new UpdatePlacementOffsetUseCase(fx.PlacementRepository);
        var occupy = new UpdatePlacementOccupySizeUseCase(fx.PlacementRepository, fx.GridRepository);
        var fork = new ForkPlacementVariantUseCase(fx.CopyRepository, fx.PlacementRepository);
        var inspectorCopyProperties = new CopyPropertiesViewModel(
            updateCopy, history, messenger, fx.ColorPicker, fx.AutoCropResolver, fx.AppSettings, loc,
            NullLogger<CopyPropertiesViewModel>.Instance);
        var inspector = new PlacementInspectorViewModel(
            offset, occupy, fork, fx.PlacementRepository, fx.CopyRepository, fx.AssetRepository, fx.Thumbnails,
            fx.Storage, inspectorCopyProperties, history, messenger, fx.AppSettings, loc,
            NullLogger<PlacementInspectorViewModel>.Instance);
        var updateWeights = new UpdateGridWeightsUseCase(fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            fx.GridRepository, fx.PlacementRepository, fx.CopyRepository, fx.AssetRepository, fx.CropResolver,
            updateWeights, NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        var variantProperties = new CopyPropertiesViewModel(
            updateCopy, history, messenger, fx.ColorPicker, fx.AutoCropResolver, fx.AppSettings, loc,
            NullLogger<CopyPropertiesViewModel>.Instance);
        var isolated = new StubIsolatedRenderer();
        var output = new GridOutputViewModel(
            render, export, picker, loc, NullLogger<GridOutputViewModel>.Instance, isolated);
        var variants = new VariantManagerViewModel(
            createCopy, updateCopy, deleteAsset, new DuplicateImageCopyUseCase(fx.CopyRepository),
            fx.CopyRepository, fx.PlacementRepository, confirmation, history, messenger, loc,
            NullLogger<VariantManagerViewModel>.Instance);
        var structure = new GridStructureEditorViewModel(
            fx.GridRepository, updateWeights, updateLocks,
            new UpdateGridStructureUseCase(fx.GridRepository, fx.PlacementRepository), fitWeight, history, loc);
        var workspace = new GridWorkspaceViewModel(
            fx.GridRepository, fx.CopyRepository, fx.AssetRepository, fx.PlacementRepository, fx.Thumbnails,
            fx.CropResolver, place, remove, move, swap, offset, fx.Storage, fx.AppSettings, messenger, history,
            inspector, variantProperties, output, variants, structure, confirmation, loc,
            NullLogger<GridWorkspaceViewModel>.Instance);
        var main = new MainWindowViewModel(assetLibrary, gridList, workspace, messenger, history, loc);

        return new AppViewModelHarness
        {
            Fx = fx, Messenger = messenger, History = history, GridList = gridList, Workspace = workspace,
            Main = main, Confirmation = confirmation, Isolated = isolated,
        };
    }

    public async Task SetAutoSaveAsync(bool enabled) =>
        await Fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = enabled });

    public async Task<GridCanvas> SeedGridAsync(int rows, int cols, string name = "grid", int canvas = 400)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(), Name = name, GridRows = rows, GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols), RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(canvas, canvas),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        (await Fx.GridRepository.AddAsync(grid)).IsError.Should().BeFalse();
        return grid;
    }

    /// <summary>
    /// 同じ画像から作った 2 つのバリアントを、 2×2 グリッドのセル (0,0) と (1,0) へ配置して読み込む。
    /// 自動保存の設定は呼び出し側が先に <see cref="SetAutoSaveAsync"/> で決める。
    /// </summary>
    public async Task<(GridCanvas Grid, ImageCopy A, ImageCopy B)> SeedTwoPlacementsAsync()
    {
        var asset = await Fx.SeedAssetAsync(width: 100, height: 100);
        var a = await Fx.SeedCopyAsync(asset.Id, "A");
        var b = await Fx.SeedCopyAsync(asset.Id, "B");
        var grid = await SeedGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(Fx.GridRepository, Fx.CopyRepository, Fx.PlacementRepository);
        (await place.ExecuteAsync(grid.Id, a.Id, new CellPosition(0, 0))).IsError.Should().BeFalse();
        (await place.ExecuteAsync(grid.Id, b.Id, new CellPosition(1, 0))).IsError.Should().BeFalse();
        await Workspace.LoadGridAsync(new GridCanvasItemViewModel(grid));
        return (grid, a, b);
    }

    public async ValueTask DisposeAsync()
    {
        Main.Dispose();
        History.Dispose();
        await Fx.DisposeAsync();
    }
}
