using System.Collections.Immutable;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.History;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class VariantAuditRegressionTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private readonly AutoConfirmationService _confirm = new();
    private WeakReferenceMessenger _messenger = null!;
    private UndoRedoService _history = null!;
    private GridWorkspaceViewModel _vm = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _messenger = new WeakReferenceMessenger();

        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        var move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var swap = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var render = new RenderGridUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository,
            _fx.AssetRepository, _fx.Storage, new SkiaGridImageRenderer(new AutoCropCache()));
        var export = new ExportGridUseCase(render);
        var picker = Substitute.For<IFilePickerService>();
        var offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        var occupy = new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository);
        var fork = new ForkPlacementVariantUseCase(_fx.CopyRepository, _fx.PlacementRepository);
        _history = new UndoRedoService();
        var updateCopyForInspector = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
        var copyPropertiesForInspector = new CopyPropertiesViewModel(
            updateCopyForInspector, _history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver, _fx.AppSettings,
            new NullLocalizationService(),
            NullLogger<CopyPropertiesViewModel>.Instance);
        var inspector = new PlacementInspectorViewModel(
            offset,
            occupy,
            fork,
            _fx.PlacementRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _fx.Thumbnails,
            _fx.Storage,
            copyPropertiesForInspector,
            _history,
            _messenger,
            _fx.AppSettings,
            new NullLocalizationService(),
            NullLogger<PlacementInspectorViewModel>.Instance);

        var updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(_fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository,
            _fx.CropResolver, updateWeights,
            NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        var createCopy = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
        var updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var variantProperties = new CopyPropertiesViewModel(
            updateCopy, _history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver, _fx.AppSettings,
            new NullLocalizationService(),
            NullLogger<CopyPropertiesViewModel>.Instance);

        // Phase 5: 子 VM 3 つを直接構築して Workspace VM に渡す (DI 経由ではなくテスト fixture では手動)。
        // Workspace は 2-phase init (AttachContext) で this を子に注入するため、 子はここでは未 attach。
        var output = new GridOutputViewModel(
            render, export, picker, new NullLocalizationService(),
            NullLogger<GridOutputViewModel>.Instance);
        var variants = new VariantManagerViewModel(
            createCopy, updateCopy, deleteAsset, _fx.CopyRepository, _fx.PlacementRepository, new AutoConfirmationService(),
            _history, _messenger, new NullLocalizationService(),
            NullLogger<VariantManagerViewModel>.Instance);
        var structure = new GridStructureEditorViewModel(
            _fx.GridRepository, updateWeights, updateLocks, fitWeight, _history,
            new NullLocalizationService());

        _vm = new GridWorkspaceViewModel(
            _fx.GridRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _fx.PlacementRepository,
            _fx.Thumbnails,
            _fx.CropResolver,
            place,
            remove,
            move,
            swap,
            offset,
            _fx.Storage,
            _fx.AppSettings,
            _messenger,
            _history,
            inspector,
            variantProperties,
            output,
            variants,
            structure, _confirm,
            new NullLocalizationService(),
            NullLogger<GridWorkspaceViewModel>.Instance);
    }

    public async Task DisposeAsync()
    {
        _vm.Dispose();
        _vm.Inspector.Dispose();
        _messenger.UnregisterAll(_vm);
        await _fx.DisposeAsync();
    }

    private async Task<GridCanvas> SeedActiveGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"workspace-{rows}x{cols}",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(400, 400),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();
        return grid;
    }


    [Fact]
    public async Task Audit_Fork_Reload_Must_Update_Placement_And_Inspector_CopyIdentity()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, original.Id, new CellPosition(0, 0));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single();
        await _vm.WaitPendingInspectorAttachAsync();

        await _vm.Inspector.ForkVariantAsync();
        await _vm.ReloadFromMessageAsyncForTests();
        var stored = (await _fx.PlacementRepository.FindByIdAsync(placed.Value.Id))!;
        stored.CopyId.Should().NotBe(original.Id, "the use case must actually fork the database placement");

        _vm.SelectedPlacement!.CopyId.Should().Be(stored.CopyId,
            "the existing placement VM must follow the new database CopyId");
        _vm.Inspector.CopyProperties.AttachedCopyId.Should().Be(stored.CopyId,
            "editing a forked placement must target its new variant");
    }

    [Fact]
    public async Task Audit_Edit_After_Fork_Must_Leave_Original_Variant_Unchanged()
    {
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, original.Id, new CellPosition(0, 0));
        await place.ExecuteAsync(grid.Id, original.Id, new CellPosition(1, 0));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single(p => p.PlacementId == placed.Value.Id);
        await _vm.WaitPendingInspectorAttachAsync();
        await _vm.Inspector.ForkVariantAsync();
        await _vm.ReloadFromMessageAsyncForTests();
        var forkId = (await _fx.PlacementRepository.FindByIdAsync(placed.Value.Id))!.CopyId;

        var cp = _vm.Inspector.CopyProperties;
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = 10;
        cp.ManualCropPixelY = 10;
        cp.ManualCropPixelWidth = 50;
        cp.ManualCropPixelHeight = 50;
        (await cp.TrySaveAsync()).Should().BeTrue();
        await _vm.ReloadFromMessageAsyncForTests();

        (await _fx.CopyRepository.FindByIdAsync(original.Id))!.ManualCrop.Should().BeNull(
            "forking must isolate the other placement from subsequent edits");
        (await _fx.CopyRepository.FindByIdAsync(forkId))!.ManualCrop.Should().NotBeNull(
            "the selected fork must receive the crop edit");
    }

    [Fact]
    public async Task Audit_Candidate_Switch_Before_AutoSave_Must_Preserve_Original_Draft()
    {
        await _fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = true });
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var a = await _fx.SeedCopyAsync(asset.Id, copyName: "A");
        var b = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidate = _vm.Candidates.Single(c => c.CopyId == a.Id);
        await WaitForVariantAttachAsync(a.Id);
        var cp = _vm.VariantProperties;
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = 10;
        cp.ManualCropPixelY = 10;
        cp.ManualCropPixelWidth = 50;
        cp.ManualCropPixelHeight = 50;
        cp.IsDirty.Should().BeTrue();

        // This is the real candidate-selection hook, not a direct Attach call.
        _vm.SelectedCandidate = _vm.Candidates.Single(c => c.CopyId == b.Id);
        await WaitForVariantAttachAsync(b.Id);
        await Task.Delay(1300); // exceed the production 1000ms debounce

        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.ManualCrop.Should().NotBeNull(
            "autosave-on selection changes must flush A before attaching B");
    }

    private async Task WaitForVariantAttachAsync(Guid copyId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_vm.VariantProperties.AttachedCopyId != copyId)
            await Task.Delay(5, timeout.Token);
    }

    [Fact]
    public async Task Audit_Rename_Must_Not_Erase_Unsaved_Crop_LivePreview()
    {
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        await place.ExecuteAsync(grid.Id, original.Id, new CellPosition(0, 0));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single();
        await _vm.WaitPendingInspectorAttachAsync();
        var cp = _vm.Inspector.CopyProperties;
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = 10;
        cp.ManualCropPixelY = 10;
        cp.ManualCropPixelWidth = 50;
        cp.ManualCropPixelHeight = 50;
        _vm.Placements.Single().EffectiveCropFraction.Should().NotBeNull();

        var candidate = _vm.Candidates.Single(c => c.CopyId == original.Id);
        _vm.Variants.BeginEditCandidate(candidate);
        candidate.EditingName = "renamed";
        await _vm.Variants.CommitEditCandidateAsync(candidate);
        await _vm.ReloadFromMessageAsyncForTests();

        cp.IsDirty.Should().BeTrue("the crop draft is still unsaved");
        cp.DraftManualCrop.Should().NotBeNull();
        _vm.Placements.Single().EffectiveCropFraction.Should().NotBeNull(
            "renaming a variant must not revert an unrelated active crop draft on the canvas");
    }

    [Fact]
    public async Task Audit_Custom_AutoCrop_Alpha_Must_Survive_Reattach_And_Unrelated_Edit()
    {
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "alpha");
        var cp = _vm.Inspector.CopyProperties;
        cp.Attach(new CopyItemViewModel(original, null, null, 100, 100));
        cp.AutoCropEnabled = true;
        cp.AutoCropPreset = AutoCropPreset.Custom;
        cp.AutoCropCustomColorHex = "#80112233";
        (await cp.TrySaveAsync()).Should().BeTrue();
        var saved = (await _fx.CopyRepository.FindByIdAsync(original.Id))!;
        saved.AutoCrop!.Value.TargetColorArgb.Should().Be(0x80112233u);

        cp.Attach(new CopyItemViewModel(saved, null, null, 100, 100));
        cp.FlipX = true; // should not change the saved auto-crop target
        (await cp.TrySaveAsync()).Should().BeTrue();

        (await _fx.CopyRepository.FindByIdAsync(original.Id))!.AutoCrop!.Value.TargetColorArgb
            .Should().Be(0x80112233u, "reattaching must preserve the alpha in an accepted #AARRGGBB target");
    }

    [Fact]
    public async Task Audit_Saved_Crop_Must_Survive_Editor_Switch_And_Bottom_Alignment_Edit()
    {
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        await place.ExecuteAsync(grid.Id, original.Id, new CellPosition(0, 0));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        await WaitForVariantAttachAsync(original.Id);
        _vm.SelectedPlacement = _vm.Placements.Single();
        await _vm.WaitPendingInspectorAttachAsync();
        var cp = _vm.Inspector.CopyProperties;
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = 10;
        cp.ManualCropPixelY = 10;
        cp.ManualCropPixelWidth = 50;
        cp.ManualCropPixelHeight = 50;
        (await cp.TrySaveAsync()).Should().BeTrue();
        await _vm.ReloadFromMessageAsyncForTests();
        (await _fx.CopyRepository.FindByIdAsync(original.Id))!.ManualCrop.Should().NotBeNull();

        // Escape in the real canvas sets SelectedPlacement=null, retains candidate selection,
        // and exposes the separate VariantProperties editor for the very same variant.
        _vm.SelectedPlacement = null;
        await _vm.WaitPendingInspectorAttachAsync();
        _vm.IsVariantSelected.Should().BeTrue();
        _vm.VariantProperties.AlignY = AnchorY.Bottom;
        (await _vm.VariantProperties.TrySaveAsync()).Should().BeTrue();

        (await _fx.CopyRepository.FindByIdAsync(original.Id))!.ManualCrop.Should().NotBeNull(
            "an alignment edit in the second editor must not erase the already-saved crop");
    }

    [Fact]
    public async Task Audit_Control_Inspector_AutoSave_CropBottom_Then_Select_Other_Placement()
    {
        await _fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = true });
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var variant = await _fx.SeedCopyAsync(asset.Id, copyName: "variant");
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var p1 = await place.ExecuteAsync(grid.Id, original.Id, new CellPosition(0, 0));
        var p2 = await place.ExecuteAsync(grid.Id, variant.Id, new CellPosition(0, 1));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single(p => p.PlacementId == p1.Value.Id);
        await _vm.WaitPendingInspectorAttachAsync();
        var cp = _vm.Inspector.CopyProperties;
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = 10;
        cp.ManualCropPixelY = 10;
        cp.ManualCropPixelWidth = 50;
        cp.ManualCropPixelHeight = 50;
        cp.AlignY = AnchorY.Bottom;
        _vm.SelectedPlacement = _vm.Placements.Single(p => p.PlacementId == p2.Value.Id);
        await _vm.WaitPendingInspectorAttachAsync();

        var stored = (await _fx.CopyRepository.FindByIdAsync(original.Id))!;
        stored.ManualCrop.Should().NotBeNull();
        stored.Alignment.Y.Should().Be(AnchorY.Bottom);
    }
}
