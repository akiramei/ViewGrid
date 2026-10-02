using CommunityToolkit.Mvvm.Messaging;
using ErrorOr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.History;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;

namespace ViewGrid.Application.Tests.ViewModels;

/// <summary>
/// 不具合調査報告 (2026-10-02) の修正に対する回帰テスト。 報告の再現テスト (<c>Audit_*</c>) が直接は
/// 押さえない経路 — 出力前の保存確定 (C02)、 見出し選択 (D01)、 crop 数値入力 (C06)、
/// 2 つの編集パネルの再同期の逆方向 (C01) — を固定する。
/// </summary>
public sealed class AuditFixRegressionTests : IAsyncLifetime
{
    /// <summary>2 つ目のアセット用 (同一ハッシュの画像は登録できないため別ハッシュにする)。</summary>
    private const string HashB = "hash000000000000000000000000000000000000000000000000000000000002";

    private UseCaseFixture _fx = null!;
    private WeakReferenceMessenger _messenger = null!;
    private UndoRedoService _history = null!;
    private GridWorkspaceViewModel _vm = null!;
    private readonly CaptureRenderer _capture = new();
    private readonly IFilePickerService _picker = Substitute.For<IFilePickerService>();

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
            _fx.AssetRepository, _fx.Storage, _capture);
        var export = new ExportGridUseCase(render);
        var offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        var occupy = new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository);
        var fork = new ForkPlacementVariantUseCase(_fx.CopyRepository, _fx.PlacementRepository);
        _history = new UndoRedoService();
        var updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
        var inspectorCopyProperties = new CopyPropertiesViewModel(
            updateCopy, _history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver, _fx.AppSettings,
            new NullLocalizationService(), NullLogger<CopyPropertiesViewModel>.Instance);
        var inspector = new PlacementInspectorViewModel(
            offset, occupy, fork,
            _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository, _fx.Thumbnails, _fx.Storage,
            inspectorCopyProperties, _history, _messenger, _fx.AppSettings,
            new NullLocalizationService(), NullLogger<PlacementInspectorViewModel>.Instance);

        var updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(_fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository,
            _fx.CropResolver, updateWeights, NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        var createCopy = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var variantProperties = new CopyPropertiesViewModel(
            updateCopy, _history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver, _fx.AppSettings,
            new NullLocalizationService(), NullLogger<CopyPropertiesViewModel>.Instance);

        var output = new GridOutputViewModel(
            render, export, _picker, new NullLocalizationService(),
            NullLogger<GridOutputViewModel>.Instance);
        var variants = new VariantManagerViewModel(
            createCopy, updateCopy, deleteAsset, _fx.CopyRepository,
            _history, _messenger, new NullLocalizationService(),
            NullLogger<VariantManagerViewModel>.Instance);
        var structure = new GridStructureEditorViewModel(
            _fx.GridRepository, updateWeights, updateLocks, fitWeight, _history,
            new NullLocalizationService());

        _vm = new GridWorkspaceViewModel(
            _fx.GridRepository, _fx.CopyRepository, _fx.AssetRepository, _fx.PlacementRepository,
            _fx.Thumbnails, _fx.CropResolver,
            place, remove, move, swap, offset,
            _fx.Storage, _fx.AppSettings, _messenger, _history,
            inspector, variantProperties, output, variants, structure,
            new NullLocalizationService(), NullLogger<GridWorkspaceViewModel>.Instance);
    }

    public async Task DisposeAsync()
    {
        _vm.Dispose();
        _messenger.UnregisterAll(_vm);
        await _fx.DisposeAsync();
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
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
        (await _fx.GridRepository.AddAsync(grid)).IsError.Should().BeFalse();
        return grid;
    }

    private async Task<(GridCanvas Grid, ImageCopy Copy, GridPlacement Placement)> SeedSelectedPlacementAsync(
        bool autoSave)
    {
        await _fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = autoSave });
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        var grid = await SeedGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placement = (await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0))).Value;
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single();
        await _vm.WaitPendingInspectorAttachAsync();
        return (grid, copy, placement);
    }

    private static void SetManualCrop(CopyPropertiesViewModel cp, int x, int y, int w, int h)
    {
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = x;
        cp.ManualCropPixelY = y;
        cp.ManualCropPixelWidth = w;
        cp.ManualCropPixelHeight = h;
    }

    // ─── C02: 出力前の保存確定 ───────────────────────────────────

    [Fact]
    public async Task C02_Preview_Flushes_Pending_AutoSave_So_The_Renderer_Sees_Crop_And_Alignment()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: true);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 10, 10, 80, 50);
        cp.AlignY = AnchorY.Bottom;
        cp.IsDirty.Should().BeTrue("auto-save のデバウンス (1 秒) 内で、 まだ保存されていない");

        var preview = await _vm.Output.RequestPreviewAsync();

        preview.Should().NotBeNull();
        var rendered = _capture.Last.Single(i => i.CopyId == copy.Id);
        rendered.Crop.Should().NotBeNull("出力は DB の値を読むので、 出力前に保留中の編集が保存されていなければならない");
        rendered.Alignment.Y.Should().Be(AnchorY.Bottom);
        cp.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task C02_Export_Flushes_Pending_AutoSave_So_The_Renderer_Sees_Crop()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: true);
        SetManualCrop(_vm.Inspector.CopyProperties, 10, 10, 80, 50);
        var path = Path.Combine(_fx.TempDir.FullName, "c02-export.png");
        _picker.PickSavePngPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(path));

        await _vm.Output.ExportToPngAsync();

        _capture.Last.Single(i => i.CopyId == copy.Id).Crop.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task C02_Preview_Flushes_Standalone_Variant_Draft_Too()
    {
        await _fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = true });
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "A");
        var grid = await SeedGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        await _vm.WaitPendingVariantAttachAsync();
        _vm.SelectedPlacement.Should().BeNull();
        _vm.IsVariantSelected.Should().BeTrue();
        SetManualCrop(_vm.VariantProperties, 10, 10, 80, 50);

        await _vm.Output.RequestPreviewAsync();

        _capture.Last.Single(i => i.CopyId == copy.Id).Crop.Should().NotBeNull(
            "候補単体編集 (VariantProperties) の保留中 auto-save も出力前に確定される");
    }

    [Fact]
    public async Task C02_Preview_Is_Aborted_When_A_Pending_Edit_Cannot_Be_Saved()
    {
        await SeedSelectedPlacementAsync(autoSave: true);
        // 2x2 グリッドに 5x5 の占有は保存できない (検証エラー) → 未保存編集が残る。
        _vm.Inspector.OccupyWidth = 5;
        _vm.Inspector.OccupyHeight = 5;
        _capture.Last = [];

        var preview = await _vm.Output.RequestPreviewAsync();

        preview.Should().BeNull("古い内容を黙って出力せず中止する");
        _capture.Last.Should().BeEmpty("レンダラーは呼ばれない");
        _vm.StatusMessage.Should().Be("Status_OutputAbortedSaveFailed");
        _vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task C02_Manual_Save_Mode_Does_Not_Save_Drafts_On_Output()
    {
        // 手動保存モードの設計判断: 出力は保存済みの値を使い、 未保存の draft を勝手に保存しない。
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        SetManualCrop(_vm.Inspector.CopyProperties, 10, 10, 80, 50);

        await _vm.Output.RequestPreviewAsync();

        _capture.Last.Single(i => i.CopyId == copy.Id).Crop.Should().BeNull();
        _vm.Inspector.CopyProperties.IsDirty.Should().BeTrue();
    }

    // ─── D01: 見出し選択 ──────────────────────────────────────────

    [Fact]
    public async Task D01_Selecting_A_Group_Header_Clears_The_Candidate_And_Disables_Candidate_Commands()
    {
        var assetA = await _fx.SeedAssetAsync();
        var assetB = await _fx.SeedAssetAsync(HashB);
        var a = await _fx.SeedCopyAsync(assetA.Id, "A");
        await _fx.SeedCopyAsync(assetB.Id, "B");
        await _vm.LoadCandidatesAsync();
        var candidateA = _vm.Candidates.Single(c => c.CopyId == a.Id);
        _vm.SelectedCandidateNode = candidateA;
        _vm.SelectedCandidate.Should().BeSameAs(candidateA);
        var headerB = _vm.CandidateGroups.Single(g => g.AssetId == assetB.Id);

        _vm.SelectedCandidateNode = headerB;

        _vm.SelectedCandidate.Should().BeNull("見出しを選んだのに前の候補が対象のまま残ってはならない");
        _vm.SelectedCandidateNode.Should().BeSameAs(headerB, "ツリー上の見出し選択は維持される");
        _vm.Variants.DeleteSelectedCandidateCommand.CanExecute(null).Should().BeFalse();
        _vm.Variants.BeginCreateVariantCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task D01_Delete_Does_Not_Touch_The_Previously_Selected_Candidate_After_Header_Selection()
    {
        var assetA = await _fx.SeedAssetAsync();
        var assetB = await _fx.SeedAssetAsync(HashB);
        var a = await _fx.SeedCopyAsync(assetA.Id, "A");
        await _fx.SeedCopyAsync(assetB.Id, "B");
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == a.Id);
        _vm.SelectedCandidateNode = _vm.CandidateGroups.Single(g => g.AssetId == assetB.Id);

        await _vm.Variants.DeleteSelectedCandidateAsync();

        (await _fx.CopyRepository.FindByIdAsync(a.Id)).Should().NotBeNull("見出し選択中の削除が A の画像・バリアントを消してはならない");
        (await _fx.AssetRepository.FindByIdAsync(assetA.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task D01_Reload_Does_Not_Steal_Header_Selection_By_Auto_Selecting_The_First_Candidate()
    {
        var assetA = await _fx.SeedAssetAsync();
        var assetB = await _fx.SeedAssetAsync(HashB);
        await _fx.SeedCopyAsync(assetA.Id, "A");
        await _fx.SeedCopyAsync(assetB.Id, "B");
        await _vm.LoadCandidatesAsync();
        var headerB = _vm.CandidateGroups.Single(g => g.AssetId == assetB.Id);
        _vm.SelectedCandidateNode = headerB;

        await _vm.LoadCandidatesAsync();

        _vm.SelectedCandidate.Should().BeNull();
        _vm.SelectedCandidateNode.Should().BeSameAs(headerB);
    }

    [Fact]
    public async Task D01_Selecting_A_Candidate_After_A_Header_Restores_Candidate_Selection()
    {
        var assetA = await _fx.SeedAssetAsync();
        var a = await _fx.SeedCopyAsync(assetA.Id, "A");
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.CandidateGroups.Single();
        _vm.SelectedCandidate.Should().BeNull();

        var candidate = _vm.Candidates.Single(c => c.CopyId == a.Id);
        _vm.SelectedCandidateNode = candidate;

        _vm.SelectedCandidate.Should().BeSameAs(candidate);
        _vm.SelectedCandidateNode.Should().BeSameAs(candidate);
    }

    // ─── C01 (逆方向): 候補単体で保存 → Inspector 側の古いスナップショットを再同期 ──────

    [Fact]
    public async Task C01_Reverse_Variant_Save_Is_Reflected_In_Inspector_Editor_And_Not_Erased_By_Its_Next_Save()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        // 配置選択中でも、 候補単体編集は同じバリアントに attach されている (非表示)。
        await _vm.WaitPendingVariantAttachAsync();
        _vm.VariantProperties.AttachedCopyId.Should().Be(copy.Id);
        SetManualCrop(_vm.VariantProperties, 10, 10, 50, 50);
        (await _vm.VariantProperties.TrySaveAsync()).Should().BeTrue();
        await _vm.ReloadFromMessageAsyncForTests();

        var inspectorCp = _vm.Inspector.CopyProperties;
        inspectorCp.ManualCropEnabled.Should().BeTrue("非表示側の保存結果を Inspector 側も取り込む");

        inspectorCp.AlignY = AnchorY.Bottom;
        (await inspectorCp.TrySaveAsync()).Should().BeTrue();

        var stored = (await _fx.CopyRepository.FindByIdAsync(copy.Id))!;
        stored.ManualCrop.Should().NotBeNull("Inspector 側の保存が、 候補側で保存した crop を消してはならない");
        stored.Alignment.Y.Should().Be(AnchorY.Bottom);
    }

    [Fact]
    public async Task C01_Reload_Keeps_An_Unsaved_Draft_In_The_Same_Editor()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 10, 10, 50, 50);

        await _vm.ReloadFromMessageAsyncForTests();

        cp.IsDirty.Should().BeTrue();
        cp.ManualCropEnabled.Should().BeTrue("再読込が編集中の draft を DB 値で上書きしてはならない");
        cp.ManualCropPixelWidth.Should().Be(50);
    }

    // ─── D03 補助: 古い読込が IsBusy を残さない ───────────────────

    [Fact]
    public async Task D03_Superseded_Load_Does_Not_Leave_IsBusy_Set()
    {
        var gridA = await SeedGridAsync(2, 2);
        var gridB = await SeedGridAsync(3, 3);

        var first = _vm.LoadGridAsync(new GridCanvasItemViewModel(gridA));
        var second = _vm.LoadGridAsync(new GridCanvasItemViewModel(gridB));
        await Task.WhenAll(first, second);

        _vm.IsBusy.Should().BeFalse();
        _vm.CurrentGrid!.GridId.Should().Be(gridB.Id);
    }

    // ─── C06: crop 数値入力 ───────────────────────────────────────

    [Fact]
    public async Task C06_Clearing_Width_Keeps_Fields_Enabled_And_Does_Not_Delete_The_Crop()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 10, 10, 50, 50);
        (await cp.TrySaveAsync()).Should().BeTrue();

        cp.ManualCropPixelWidth = null; // 数値欄を選択して消した状態

        cp.IsManualCropInputEnabled.Should().BeTrue("空欄で全数値欄が無効化されて入力を続けられなくなってはならない");
        cp.IsManualCropDefined.Should().BeTrue("空欄は入力し直しの途中で、 crop の消失ではない");
        cp.DraftManualCrop.Should().NotBeNull();
        cp.DraftManualCrop!.Value.Width.Should().BeApproximately(0.5, 1e-9, "空欄の間は直前の有効値を使う");

        cp.ManualCropPixelWidth = 30; // 再入力
        (await cp.TrySaveAsync()).Should().BeTrue();
        var stored = (await _fx.CopyRepository.FindByIdAsync(cp.AttachedCopyId!.Value))!;
        stored.ManualCrop.Should().NotBeNull();
        stored.ManualCrop!.Value.Width.Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public async Task C06_Saving_While_A_Field_Is_Blank_Does_Not_Erase_The_Stored_Crop()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 10, 10, 50, 50);
        (await cp.TrySaveAsync()).Should().BeTrue();

        cp.ManualCropPixelHeight = null;
        cp.FlipX = true; // 別項目を変更して保存 (auto-save が空欄の最中に走る状況)
        (await cp.TrySaveAsync()).Should().BeTrue();

        (await _fx.CopyRepository.FindByIdAsync(cp.AttachedCopyId!.Value))!.ManualCrop
            .Should().NotBeNull("空欄の最中の保存で crop が null (削除) にならない");
    }

    [Fact]
    public async Task C06_Zero_Width_Is_Undefined_But_Input_Stays_Enabled_And_Recovers()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 10, 10, 50, 50);

        cp.ManualCropPixelWidth = 0;
        cp.IsManualCropDefined.Should().BeFalse("0 は従来どおり「未確定」");
        cp.IsManualCropInputEnabled.Should().BeTrue();

        cp.ManualCropPixelWidth = 40;
        cp.IsManualCropDefined.Should().BeTrue();
    }

    [Fact]
    public async Task C06_Width_Beyond_The_Image_Is_Clamped_So_Dragging_Cannot_Throw()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties; // 100x100 の画像
        cp.ManualCropEnabled = true;
        cp.ManualCropPixelX = 0;
        cp.ManualCropPixelY = 0;
        cp.ManualCropPixelHeight = 50;

        cp.ManualCropPixelWidth = 200; // 画像より大きい

        cp.ManualCropPixelWidth.Should().Be(100);
        // View のドラッグ移動は Math.Clamp(x, 0, sourceWidth - W) を使う。 W <= sourceWidth なら上限は負にならない。
        (cp.SourceWidth - cp.ManualCropPixelWidth!.Value).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task C06_Moving_The_Origin_Past_The_Right_Edge_Trims_The_Width_And_Updates_The_Field()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 0, 0, 40, 40);

        cp.ManualCropPixelX = 80;

        cp.ManualCropPixelWidth.Should().Be(20, "原点 80 + 幅 40 は画像 (100) を超えるので切り詰める");
        cp.GetManualCropRect().Should().Be((80, 0, 20, 40));
    }

    [Fact]
    public async Task C06_Origin_Is_Clamped_Inside_The_Image()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 0, 0, 40, 40);

        cp.ManualCropPixelX = 500;
        cp.ManualCropPixelY = 500;

        cp.ManualCropPixelX.Should().Be(99);
        cp.ManualCropPixelY.Should().Be(99);
        cp.ManualCropPixelWidth.Should().Be(1);
        cp.ManualCropPixelHeight.Should().Be(1);
    }

    [Fact]
    public async Task C06_Numeric_Input_Is_Disabled_Only_When_Not_In_Manual_Mode()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var cp = _vm.Inspector.CopyProperties;

        cp.IsManualCropInputEnabled.Should().BeFalse("手動モードでない");
        cp.ManualCropEnabled = true;
        cp.IsManualCropInputEnabled.Should().BeTrue("矩形が未確定 (0x0) でも入力はできる");
    }

    // ─── D05 補助: α の往復 ──────────────────────────────────────

    [Theory]
    [InlineData(0x00112233u, "#00112233")]
    [InlineData(0x80112233u, "#80112233")]
    [InlineData(0xFF112233u, "#112233")]
    public void D05_FormatHex_Is_Lossless_And_Round_Trips(uint argb, string expected)
    {
        var hex = CopyPropertiesViewModel.FormatHex(argb);

        hex.Should().Be(expected);
        CopyPropertiesViewModel.ParseHexColorOrDefault(hex).Should().Be(argb);
    }

    // ─── C03: Fork 後の identity 追従 ────────────────────────────

    [Fact]
    public async Task C03_Fork_Updates_Label_And_CopyId_Of_The_Reused_Placement_ViewModel()
    {
        var (_, original, placement) = await SeedSelectedPlacementAsync(autoSave: false);
        var before = _vm.Placements.Single();
        var labelBefore = before.Label;

        await _vm.Inspector.ForkVariantAsync();
        await _vm.ReloadFromMessageAsyncForTests();

        var after = _vm.Placements.Single();
        after.Should().BeSameAs(before, "PlacementId が同じ VM は再利用される (参照同一性)");
        after.PlacementId.Should().Be(placement.Id);
        after.CopyId.Should().NotBe(original.Id);
        after.Label.Should().NotBe(labelBefore, "ラベルも新しいバリアント名 (派生) に追従する");
    }

    private sealed class CaptureRenderer : IGridImageRenderer
    {
        public List<(Guid CopyId, ManualCropFraction? Crop, Alignment Alignment)> Last { get; set; } = [];

        public Task<ErrorOr<byte[]>> RenderPngAsync(
            GridCanvas grid, IReadOnlyList<PlacementRenderItem> items, RenderOptions options,
            CancellationToken ct = default)
        {
            Last = items.Select(i => (i.Copy.Id, i.Copy.ManualCrop, i.Copy.Alignment)).ToList();
            return new SkiaGridImageRenderer(new AutoCropCache()).RenderPngAsync(grid, items, options, ct);
        }
    }
}
