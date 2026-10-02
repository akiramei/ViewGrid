using CommunityToolkit.Mvvm.Messaging;
using ErrorOr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.History;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Preview;
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
    private readonly StubIsolatedRenderer _isolated = new();
    private readonly AutoConfirmationService _confirm = new();
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
        var createCopy = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository, _fx.AppSettings);
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var variantProperties = new CopyPropertiesViewModel(
            updateCopy, _history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver, _fx.AppSettings,
            new NullLocalizationService(), NullLogger<CopyPropertiesViewModel>.Instance);

        var output = new GridOutputViewModel(
            render, export, _picker, new NullLocalizationService(),
            NullLogger<GridOutputViewModel>.Instance, _isolated);
        output.LivePreviewDebounce = TimeSpan.FromMilliseconds(30);
        var variants = new VariantManagerViewModel(
            createCopy, updateCopy, deleteAsset, new DuplicateImageCopyUseCase(_fx.CopyRepository), _fx.CopyRepository, _fx.PlacementRepository, _confirm,
            _history, _messenger, new NullLocalizationService(),
            NullLogger<VariantManagerViewModel>.Instance);
        var structure = new GridStructureEditorViewModel(
            _fx.GridRepository, updateWeights, updateLocks, new UpdateGridStructureUseCase(_fx.GridRepository, _fx.PlacementRepository), fitWeight, _history,
            new NullLocalizationService());

        _vm = new GridWorkspaceViewModel(
            _fx.GridRepository, _fx.CopyRepository, _fx.AssetRepository, _fx.PlacementRepository,
            _fx.Thumbnails, _fx.CropResolver,
            place, remove, move, swap, offset,
            _fx.Storage, _fx.AppSettings, _messenger, _history,
            inspector, variantProperties, output, variants, structure, _confirm,
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

    // ─── 手動保存モードの切替時: 保存 / 破棄 / 戻る (ユーザビリティ評価: 切替で無言で消える) ──────────

    /// <summary>2 つの配置 (バリアント A / B) を作り、 A の配置を選択して Inspector に attach 済みにする。</summary>
    private async Task<(ImageCopy A, ImageCopy B, PlacementItemViewModel P1, PlacementItemViewModel P2)>
        SeedTwoPlacementsAsync(bool autoSave)
    {
        await _fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = autoSave });
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var a = await _fx.SeedCopyAsync(asset.Id, copyName: "A");
        var b = await _fx.SeedCopyAsync(asset.Id, copyName: "B");
        var grid = await SeedGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var pa = (await place.ExecuteAsync(grid.Id, a.Id, new CellPosition(0, 0))).Value;
        var pb = (await place.ExecuteAsync(grid.Id, b.Id, new CellPosition(1, 0))).Value;
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        var p1 = _vm.Placements.Single(p => p.PlacementId == pa.Id);
        var p2 = _vm.Placements.Single(p => p.PlacementId == pb.Id);
        await SwitchToAsync(p1);
        return (a, b, p1, p2);
    }

    /// <summary>View のバインドと同じく選択プロパティを書き換え、 切替の連鎖 (確認・attach) の完了を待つ。</summary>
    private async Task SwitchToAsync(PlacementItemViewModel? placement)
    {
        _vm.SelectedPlacement = placement;
        await _vm.WaitPendingInspectorAttachAsync();
        await _vm.WaitPendingVariantAttachAsync();
    }

    /// <summary>A の配置に未保存の編集 (共有特性の縦揃え Bottom + 配置固有の ΔX=15) を作る。</summary>
    private void MakeDraft()
    {
        _vm.Inspector.CopyProperties.AlignY = AnchorY.Bottom;
        _vm.Inspector.PixelOffsetX = 15;
        _vm.Inspector.IsAnyDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Switch_Cancel_Restores_The_Selection_And_Keeps_The_Draft()
    {
        var (a, _, p1, p2) = await SeedTwoPlacementsAsync(autoSave: false);
        MakeDraft();
        _confirm.UnsavedAnswer = UnsavedChoice.Cancel;

        await SwitchToAsync(p2);

        _confirm.UnsavedRequests.Should().ContainSingle();
        _vm.SelectedPlacement.Should().BeSameAs(p1, "「戻る」で元の配置へ戻る");
        _vm.Inspector.AttachedSource.Should().BeSameAs(p1);
        _vm.SelectedCandidate!.CopyId.Should().Be(a.Id, "候補の選択も元のバリアントへ戻る");
        _vm.Inspector.CopyProperties.AlignY.Should().Be(AnchorY.Bottom, "編集は残る");
        _vm.Inspector.PixelOffsetX.Should().Be(15, "配置固有の編集も残る (attach をやり直さない)");
        _vm.Inspector.IsAnyDirty.Should().BeTrue();
        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.Alignment.Y.Should().Be(AnchorY.Center, "DB は変わらない");
    }

    [Fact]
    public async Task Switch_Save_Persists_The_Draft_Then_Moves_On()
    {
        var (a, _, p1, p2) = await SeedTwoPlacementsAsync(autoSave: false);
        MakeDraft();
        _confirm.UnsavedAnswer = UnsavedChoice.Save;

        await SwitchToAsync(p2);

        _confirm.UnsavedRequests.Should().ContainSingle();
        _vm.SelectedPlacement.Should().BeSameAs(p2);
        _vm.Inspector.AttachedSource.Should().BeSameAs(p2);
        _vm.Inspector.IsAnyDirty.Should().BeFalse();
        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.Alignment.Y.Should().Be(AnchorY.Bottom);
        (await _fx.PlacementRepository.FindByIdAsync(p1.PlacementId))!.PixelOffsetX.Should().Be(15);
    }

    [Fact]
    public async Task Switch_Discard_Rolls_Back_The_Draft_And_The_Canvas_Then_Moves_On()
    {
        var (a, _, p1, p2) = await SeedTwoPlacementsAsync(autoSave: false);
        MakeDraft();
        p1.Alignment.Y.Should().Be(AnchorY.Bottom, "ライブプレビューで draft が canvas に出ている");
        _confirm.UnsavedAnswer = UnsavedChoice.Discard;

        await SwitchToAsync(p2);
        await _vm.WaitPendingRollbackForTests();

        _confirm.UnsavedRequests.Should().ContainSingle();
        _vm.SelectedPlacement.Should().BeSameAs(p2);
        _vm.Inspector.IsAnyDirty.Should().BeFalse();
        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.Alignment.Y.Should().Be(AnchorY.Center);
        p1.Alignment.Y.Should().Be(AnchorY.Center, "canvas も DB の値へ戻る");
        p1.PixelOffsetX.Should().Be(0);
    }

    [Fact]
    public async Task Deselect_With_A_Draft_Asks_And_Cancel_Keeps_The_Selection()
    {
        var (_, _, p1, _) = await SeedTwoPlacementsAsync(autoSave: false);
        MakeDraft();
        _confirm.UnsavedAnswer = UnsavedChoice.Cancel;

        await SwitchToAsync(null); // Esc で選択解除

        _confirm.UnsavedRequests.Should().ContainSingle();
        _vm.SelectedPlacement.Should().BeSameAs(p1);
        _vm.Inspector.IsAnyDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Switch_Without_A_Draft_Does_Not_Ask()
    {
        var (_, _, _, p2) = await SeedTwoPlacementsAsync(autoSave: false);

        await SwitchToAsync(p2);

        _confirm.UnsavedRequests.Should().BeEmpty();
        _vm.SelectedPlacement.Should().BeSameAs(p2);
    }

    [Fact]
    public async Task Switch_With_AutoSave_On_Saves_Without_Asking()
    {
        var (a, _, _, p2) = await SeedTwoPlacementsAsync(autoSave: true);
        _vm.Inspector.CopyProperties.AlignY = AnchorY.Bottom;

        await SwitchToAsync(p2);

        _confirm.UnsavedRequests.Should().BeEmpty("自動保存 ON は従来どおり切替時に確定する");
        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.Alignment.Y.Should().Be(AnchorY.Bottom);
    }

    [Fact]
    public async Task Switch_Save_Failure_Is_Treated_As_Back_And_Keeps_The_Draft()
    {
        var (_, _, p1, p2) = await SeedTwoPlacementsAsync(autoSave: false);
        _vm.Inspector.OccupyWidth = 5; // 2x2 グリッドには置けない (検証エラー)
        _vm.Inspector.OccupyHeight = 5;
        _confirm.UnsavedAnswer = UnsavedChoice.Save;

        await SwitchToAsync(p2);

        _vm.SelectedPlacement.Should().BeSameAs(p1, "保存に失敗した編集を黙って捨てて先へ進まない");
        _vm.Inspector.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Candidate_Click_While_A_Placement_Is_Dirty_Asks_Once_And_Cancel_Restores_Both_Selections()
    {
        var (a, b, p1, _) = await SeedTwoPlacementsAsync(autoSave: false);
        MakeDraft();
        _confirm.UnsavedAnswer = UnsavedChoice.Cancel;
        var candidateB = _vm.Candidates.Single(c => c.CopyId == b.Id);

        _vm.SelectedCandidateNode = candidateB; // 配置選択中に別バリアントをクリック (配置解除 + 候補切替の 2 連鎖)
        await _vm.WaitPendingInspectorAttachAsync();
        await _vm.WaitPendingVariantAttachAsync();

        _confirm.UnsavedRequests.Should().ContainSingle("1 回の操作で確認は 1 回だけ");
        _vm.SelectedPlacement.Should().BeSameAs(p1);
        _vm.SelectedCandidate!.CopyId.Should().Be(a.Id);
        _vm.VariantProperties.AttachedCopyId.Should().Be(a.Id, "候補単体編集も元のバリアントのまま");
        _vm.Inspector.IsAnyDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Candidate_Switch_With_A_Standalone_Draft_Cancel_Keeps_It_And_Save_And_Discard_Work()
    {
        await _fx.AppSettings.UpdateAsync(s => s with { EnableAutoSave = false });
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var a = await _fx.SeedCopyAsync(asset.Id, "A");
        var b = await _fx.SeedCopyAsync(asset.Id, "B");
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == a.Id);
        await _vm.WaitPendingVariantAttachAsync();
        var candidateB = _vm.Candidates.Single(c => c.CopyId == b.Id);
        SetManualCrop(_vm.VariantProperties, 10, 10, 50, 50);

        // 戻る
        _confirm.UnsavedAnswer = UnsavedChoice.Cancel;
        _vm.SelectedCandidateNode = candidateB;
        await _vm.WaitPendingVariantAttachAsync();
        _vm.SelectedCandidate!.CopyId.Should().Be(a.Id);
        _vm.VariantProperties.IsDirty.Should().BeTrue();
        _vm.VariantProperties.ManualCropEnabled.Should().BeTrue();
        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.ManualCrop.Should().BeNull();

        // 見出しをクリックしても同じ (候補が空になる切替)
        _vm.SelectedCandidateNode = _vm.CandidateGroups.Single();
        await _vm.WaitPendingVariantAttachAsync();
        _vm.SelectedCandidate!.CopyId.Should().Be(a.Id, "見出しクリックの切替も「戻る」で元へ戻る");
        _vm.SelectedCandidateNode.Should().BeSameAs(_vm.SelectedCandidate);

        // 保存
        _confirm.UnsavedAnswer = UnsavedChoice.Save;
        _vm.SelectedCandidateNode = candidateB;
        await _vm.WaitPendingVariantAttachAsync();
        _vm.SelectedCandidate.Should().BeSameAs(candidateB);
        (await _fx.CopyRepository.FindByIdAsync(a.Id))!.ManualCrop.Should().NotBeNull();

        // 破棄 (B に新しい draft → A へ戻る)
        SetManualCrop(_vm.VariantProperties, 5, 5, 20, 20);
        _confirm.UnsavedAnswer = UnsavedChoice.Discard;
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == a.Id);
        await _vm.WaitPendingVariantAttachAsync();
        (await _fx.CopyRepository.FindByIdAsync(b.Id))!.ManualCrop.Should().BeNull();
        _vm.VariantProperties.AttachedCopyId.Should().Be(a.Id);
    }

    [Fact]
    public async Task Reselecting_The_Same_Placement_Does_Not_Ask()
    {
        var (_, _, p1, _) = await SeedTwoPlacementsAsync(autoSave: false);
        MakeDraft();

        await SwitchToAsync(p1);

        _confirm.UnsavedRequests.Should().BeEmpty();
        _vm.Inspector.IsAnyDirty.Should().BeTrue();
    }

    // ─── 削除の確認 (ユーザビリティ評価: 確認なしの不可逆削除) ───────────────────

    [Fact]
    public async Task Delete_Variant_Asks_With_Placement_Count_And_Deletes_On_Confirm()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        var second = await _fx.SeedCopyAsync(copy.AssetId, "second"); // 最後のバリアントではない
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == copy.Id);

        await _vm.Variants.DeleteSelectedCandidateAsync();

        _confirm.Requests.Should().ContainSingle();
        _confirm.Requests[0].Title.Should().Be("Confirm_DeleteVariant_Title");
        _confirm.Requests[0].Message.Should().Be("Confirm_DeleteVariant_MessageFmt(original,1)");
        (await _fx.CopyRepository.FindByIdAsync(copy.Id)).Should().BeNull();
        (await _fx.CopyRepository.FindByIdAsync(second.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_Last_Variant_Warns_That_The_Image_Itself_Is_Deleted()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == copy.Id);

        await _vm.Variants.DeleteSelectedCandidateAsync();

        _confirm.Requests.Should().ContainSingle();
        _confirm.Requests[0].Message.Should().StartWith("Confirm_DeleteLastVariant_MessageFmt(original,");
        _confirm.Requests[0].Message.Should().EndWith(",1)", "最後に配置件数 1 が渡る");
    }

    [Fact]
    public async Task Delete_Variant_Declined_Changes_Nothing()
    {
        var (_, copy, placement) = await SeedSelectedPlacementAsync(autoSave: false);
        await _fx.SeedCopyAsync(copy.AssetId, "second");
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == copy.Id);
        _history.Clear();
        // 履歴に 1 件積む (取消が履歴を全消去しないことを確認するため)。
        await _vm.MoveOrSwapPlacementAsync(placement.Id, new CellPosition(1, 1));
        _history.CanUndo.Should().BeTrue();
        _confirm.Answer = false;

        await _vm.Variants.DeleteSelectedCandidateAsync();

        (await _fx.CopyRepository.FindByIdAsync(copy.Id)).Should().NotBeNull("取消でバリアントは消えない");
        (await _fx.PlacementRepository.FindByIdAsync(placement.Id)).Should().NotBeNull("配置も消えない");
        _vm.Candidates.Should().Contain(c => c.CopyId == copy.Id);
        _vm.IsBusy.Should().BeFalse();
        _history.CanUndo.Should().BeTrue("取消した削除は履歴を全消去しない");
    }

    [Fact]
    public async Task Delete_Variant_Declined_Keeps_The_Asset_Even_For_The_Last_Variant()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == copy.Id);
        _confirm.Answer = false;

        await _vm.Variants.DeleteSelectedCandidateAsync();

        (await _fx.AssetRepository.FindByIdAsync(copy.AssetId)).Should().NotBeNull("画像本体も消えない");
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

    // ─── 候補の複製・識別 (ユーザビリティ評価) ───────────────────────────────

    [Fact]
    public async Task Duplicate_Selected_Candidate_Selects_The_Copy_Keeps_Settings_And_Leaves_The_Original()
    {
        var asset = await _fx.SeedAssetAsync(width: 100, height: 100);
        var source = await _fx.SeedCopyAsync(asset.Id, "人物");
        var edited = (await _fx.CopyRepository.FindByIdAsync(source.Id))!;
        await _fx.CopyRepository.UpdateAsync(new ImageCopy
        {
            Id = edited.Id, AssetId = edited.AssetId, CopyName = edited.CopyName,
            Transform = new ImageTransform(Rotation.Cw90, false, false),
            ScalingMode = ScalingMode.UniformCover, Alignment = edited.Alignment, OccupySize = edited.OccupySize,
            ManualCrop = new ManualCropFraction(0.1, 0.1, 0.5, 0.5),
            CreatedAt = edited.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == source.Id);
        await _vm.WaitPendingVariantAttachAsync();

        await _vm.Variants.DuplicateSelectedCandidateAsync();

        _vm.Candidates.Should().HaveCount(2);
        var duplicate = _vm.SelectedCandidate!;
        duplicate.CopyId.Should().NotBe(source.Id, "複製された候補が選択される");
        duplicate.CopyName.Should().Be("Variant_DuplicateNameFmt(人物)");
        var stored = (await _fx.CopyRepository.FindByIdAsync(duplicate.CopyId))!;
        stored.ManualCrop.Should().NotBeNull("crop を引き継ぐ");
        stored.Transform.Rotation.Should().Be(Rotation.Cw90);
        (await _fx.CopyRepository.FindByIdAsync(source.Id))!.CopyName.Should().Be("人物");
    }

    [Fact]
    public async Task Duplicate_Does_Not_Clear_The_Undo_History()
    {
        var (_, copy, placement) = await SeedSelectedPlacementAsync(autoSave: false);
        await _vm.MoveOrSwapPlacementAsync(placement.Id, new CellPosition(1, 1));
        _history.CanUndo.Should().BeTrue();
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.Candidates.Single(c => c.CopyId == copy.Id);

        await _vm.Variants.DuplicateSelectedCandidateAsync();

        _history.CanUndo.Should().BeTrue("新しい候補は既存の履歴コマンドの参照を壊さない");
    }

    [Fact]
    public async Task Duplicate_Is_Disabled_Without_A_Candidate_Selection()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, "A");
        await _vm.LoadCandidatesAsync();
        _vm.SelectedCandidateNode = _vm.CandidateGroups.Single(); // 見出し

        _vm.Variants.DuplicateSelectedCandidateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Candidate_Badges_Show_The_Crop_And_Regions_And_Follow_Saved_Changes()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        await _vm.LoadCandidatesAsync();
        var candidate = _vm.Candidates.Single(c => c.CopyId == copy.Id);
        candidate.HasBadges.Should().BeFalse("加工が無ければバッジは出ない");

        SetManualCrop(_vm.Inspector.CopyProperties, 10, 10, 50, 40);
        (await _vm.Inspector.CopyProperties.TrySaveAsync()).Should().BeTrue();
        await _vm.ReloadFromMessageAsyncForTests();

        candidate.HasBadges.Should().BeTrue("保存後の再読込で、 既存の候補 VM のバッジも更新される");
        candidate.BadgeLine.Should().Contain("50").And.Contain("40", "crop の px 寸法が分かる");
    }

    [Fact]
    public async Task Candidate_Summary_Rotation_Follows_Saved_Changes()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        await _vm.LoadCandidatesAsync();
        var candidate = _vm.Candidates.Single(c => c.CopyId == copy.Id);
        candidate.SummaryLine.Should().EndWith("0°");

        _vm.Inspector.CopyProperties.Rotation = Rotation.Cw90;
        (await _vm.Inspector.CopyProperties.TrySaveAsync()).Should().BeTrue();
        await _vm.ReloadFromMessageAsyncForTests();

        candidate.SummaryLine.Should().EndWith("90°", "表示済みの要約値も保存後に同期する");
    }

    // ─── 行・列の追加 (ユーザビリティ評価) ──────────────────────────────────

    [Fact]
    public async Task AddRow_And_AddColumn_Keep_Placements_Update_The_ViewModel_And_Undo_Restores_The_Grid()
    {
        var (a, _, p1, p2) = await SeedTwoPlacementsAsync(autoSave: false);
        var grid = _vm.CurrentGrid!;
        grid.Rows.Should().Be(2);
        grid.Cols.Should().Be(2);

        (await _vm.Structure.AddRowAsync()).Should().BeTrue();
        (await _vm.Structure.AddColumnAsync()).Should().BeTrue();

        grid.Rows.Should().Be(3);
        grid.Cols.Should().Be(3);
        grid.GridSizeLabel.Should().Contain("3×3");
        grid.RowWeights.Should().HaveCount(3);
        grid.ColWeights.Should().HaveCount(3);
        _vm.Placements.Select(p => p.PlacementId).Should().BeEquivalentTo([p1.PlacementId, p2.PlacementId]);
        (await _fx.PlacementRepository.FindByIdAsync(p2.PlacementId))!.Position.Should().Be(new CellPosition(1, 0));
        _history.History.Should().HaveCount(2, "行追加と列追加はそれぞれ 1 操作として履歴に積まれる");

        (await _history.UndoAsync()).IsError.Should().BeFalse();
        (await _history.UndoAsync()).IsError.Should().BeFalse();

        var stored = (await _fx.GridRepository.FindByIdAsync(grid.GridId))!;
        stored.GridRows.Should().Be(2);
        stored.GridCols.Should().Be(2);
        (await _fx.PlacementRepository.FindByGridIdAsync(grid.GridId)).Should().HaveCount(2, "配置は無傷");
        _ = a;
    }

    [Fact]
    public async Task AddRow_Can_Be_Redone()
    {
        await SeedTwoPlacementsAsync(autoSave: false);
        var gridId = _vm.CurrentGrid!.GridId;
        await _vm.Structure.AddRowAsync();
        await _history.UndoAsync();

        (await _history.RedoAsync()).IsError.Should().BeFalse();

        (await _fx.GridRepository.FindByIdAsync(gridId))!.GridRows.Should().Be(3);
    }

    [Fact]
    public async Task A_Placement_Can_Be_Put_Into_The_Added_Cell_And_Undo_Order_Stays_Valid()
    {
        var (_, b, _, _) = await SeedTwoPlacementsAsync(autoSave: false);
        var gridId = _vm.CurrentGrid!.GridId;
        await _vm.Structure.AddRowAsync();
        var candidateB = _vm.Candidates.Single(c => c.CopyId == b.Id);
        (await _vm.PlaceCopyAtAsync(candidateB.CopyId, new CellPosition(0, 2))).Should().BeTrue(); // 追加した行

        // 追加行の配置が先に Undo され、 続けて行追加を Undo できる (範囲外の配置が残らない)。
        (await _history.UndoAsync()).IsError.Should().BeFalse();
        (await _history.UndoAsync()).IsError.Should().BeFalse();

        (await _fx.GridRepository.FindByIdAsync(gridId))!.GridRows.Should().Be(2);
    }

    [Fact]
    public async Task AddRow_At_The_Limit_Reports_It_And_Does_Not_Touch_The_History()
    {
        await SeedTwoPlacementsAsync(autoSave: false);
        var gridId = _vm.CurrentGrid!.GridId;
        var current = GridStructure.From((await _fx.GridRepository.FindByIdAsync(gridId))!);
        var atLimit = current with
        {
            Rows = UpdateGridStructureUseCase.MaxGridDimension,
            RowWeights = GridCanvas.UniformWeights(UpdateGridStructureUseCase.MaxGridDimension),
            RowLocked = GridCanvas.AllUnlocked(UpdateGridStructureUseCase.MaxGridDimension),
        };
        await new UpdateGridStructureUseCase(_fx.GridRepository, _fx.PlacementRepository).ExecuteAsync(gridId, atLimit);
        _history.Clear();

        (await _vm.Structure.AddRowAsync()).Should().BeFalse();

        _vm.StatusMessage.Should().Be("Status_GridRowsAtLimitFmt(20)", "上限であることを利用者へ伝える");
        _history.History.Should().BeEmpty();
    }

    // ─── 開きっぱなしプレビューの自動更新 (ユーザビリティ評価: 見本を見ながら調整する) ─────────────────

    /// <summary>自動更新を始め、 起動直後に残っている非同期の通知 (選択の再読込など) を流し切ってから返す。</summary>
    private async Task<(IDisposable Handle, List<byte[]?> Received)> StartLiveAsync()
    {
        var received = new List<byte[]?>();
        var handle = _vm.Output.StartLivePreview(b => { lock (received) received.Add(b); });
        await Task.Delay(300);
        _isolated.Calls.Clear();
        lock (received) received.Clear();
        return (handle, received);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public async Task Live_Preview_Refreshes_After_A_Committed_Edit_But_Not_For_An_Unsaved_Draft()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var (handle, received) = await StartLiveAsync();
        using var live = handle;
        var cp = _vm.Inspector.CopyProperties;

        SetManualCrop(cp, 10, 10, 80, 50);
        await Task.Delay(400);
        _isolated.Count.Should().Be(0, "手動保存の draft は保存済みの値ではないので、 プレビューを更新しない");

        (await cp.TrySaveAsync()).Should().BeTrue();

        (await WaitUntilAsync(() => _isolated.Count >= 1)).Should().BeTrue("保存で履歴が積まれたらプレビューを作り直す");
        (await WaitUntilAsync(() => { lock (received) return received.Count >= 1; })).Should().BeTrue();
        lock (received) received[^1].Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Live_Preview_Does_Not_Flush_Pending_Edits_Or_Hold_Busy()
    {
        var (_, copy, _) = await SeedSelectedPlacementAsync(autoSave: false);
        var (handle, _) = await StartLiveAsync();
        using var live = handle;
        var cp = _vm.Inspector.CopyProperties;
        SetManualCrop(cp, 10, 10, 80, 50);

        _vm.Output.SelectedTrimMode = TrimMode.OccupiedCells; // 出力オプションの変更で再描画を要求する

        (await WaitUntilAsync(() => _isolated.Count >= 1)).Should().BeTrue();
        cp.IsDirty.Should().BeTrue("自動更新は未保存の編集を勝手に保存しない (履歴を積まない)");
        (await _fx.CopyRepository.FindByIdAsync(copy.Id))!.ManualCrop.Should().BeNull();
        _vm.IsBusy.Should().BeFalse("画面を止めない");
        _history.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Live_Preview_Follows_Output_Options_And_Coalesces_A_Burst_Into_One_Render()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var (handle, _) = await StartLiveAsync();
        using var live = handle;

        _vm.Output.SelectedOutputMode = OutputMode.PhotoBoard;
        _vm.Output.SelectedPhotoBoardIntensity = 0.7;
        _vm.Output.SelectedPhotoBoardIntensity = 0.8;
        _vm.Output.SelectedTrimMode = TrimMode.DrawnPixels;

        (await WaitUntilAsync(() => _isolated.Count >= 1)).Should().BeTrue();
        await Task.Delay(300);
        _isolated.Count.Should().Be(1, "連続した変更は静止後の 1 回にまとまる");
        var last = _isolated.Calls[^1].Options;
        last.OutputMode.Should().Be(OutputMode.PhotoBoard);
        last.TrimMode.Should().Be(TrimMode.DrawnPixels);
    }

    [Fact]
    public async Task Live_Preview_Follows_The_Displayed_Grid()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var (handle, _) = await StartLiveAsync();
        using var live = handle;
        var other = await SeedGridAsync(3, 3);

        await _vm.LoadGridAsync(new GridCanvasItemViewModel(other));

        (await WaitUntilAsync(() => _isolated.Count >= 1)).Should().BeTrue();
        _isolated.Calls[^1].GridId.Should().Be(other.Id, "開いているプレビューは表示中のグリッドへ追随する");
    }

    [Fact]
    public async Task Live_Preview_Stops_After_It_Is_Disposed()
    {
        await SeedSelectedPlacementAsync(autoSave: false);
        var (handle, received) = await StartLiveAsync();

        handle.Dispose();
        _vm.Output.SelectedTrimMode = TrimMode.OccupiedCells;
        await Task.Delay(400);

        _isolated.Count.Should().Be(0);
        lock (received) received.Should().BeEmpty("閉じたプレビューへは通知しない");
    }
}
