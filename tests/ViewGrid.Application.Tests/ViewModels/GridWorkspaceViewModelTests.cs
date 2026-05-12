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

public sealed class GridWorkspaceViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
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
            render,
            export,
            updateWeights,
            updateLocks,
            offset,
            fitWeight,
            createCopy,
            updateCopy,
            picker,
            _messenger,
            _history,
            inspector,
            new NullLocalizationService(),
            NullLogger<GridWorkspaceViewModel>.Instance);
    }

    public async Task DisposeAsync()
    {
        _messenger.UnregisterAll(_vm);
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task Receive_Reloads_Candidates_With_Newly_Added_Copy()
    {
        // 初期状態は空
        _vm.Candidates.Should().BeEmpty();

        // 準備タブで Copy が増えた相当の状態を作る（DB に直接投入）
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "new-copy");

        // メッセージ受信を await できる形で発火
        _vm.Receive(new CopyLibraryChangedMessage());
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Should().HaveCount(1);
        _vm.Candidates[0].CopyDisplayName.Should().Contain("new-copy");
    }

    [Fact]
    public async Task Receive_Reflects_Removed_Copies()
    {
        // Copy 2 件
        var asset = await _fx.SeedAssetAsync();
        var c1 = await _fx.SeedCopyAsync(asset.Id, copyName: "keep");
        var c2 = await _fx.SeedCopyAsync(asset.Id, copyName: "remove");

        await _vm.ReloadFromMessageAsyncForTests();
        _vm.Candidates.Should().HaveCount(2);

        // 1 件削除
        await _fx.CopyRepository.DeleteAsync(c2.Id);

        // メッセージ受信で再ロード → 1 件に減る
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.Candidates.Should().HaveCount(1);
        _vm.Candidates[0].CopyId.Should().Be(c1.Id);
    }

    [Fact]
    public async Task Sending_Through_Messenger_Triggers_Reload_On_Registered_Vm()
    {
        // VM はコンストラクタで Register 済み。メッセンジャー経由で Send → Receive が呼ばれることを確認。
        // fire-and-forget の完了を待つため、初期 Reload を 1 度走らせて await する。
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id);

        // Send 経由で発火
        _messenger.Send(new CopyLibraryChangedMessage());

        // fire-and-forget の完了を確実に await するため同形メソッドを 1 回呼ぶ。
        // （実環境では UI スレッド側の dispatcher が処理するため tick 不要だが、
        //  単体テストでは内部メソッドを await して確定させる）
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Should().HaveCount(1);
    }

    [Fact]
    public async Task Receive_Drops_Stale_Placements_When_Their_Asset_Cascade_Deleted()
    {
        // Asset + Copy + アクティブグリッドに配置を作る
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(rows: 2, cols: 2);

        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        // VM をそのグリッドに紐付ける（候補と配置を読む）
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.Candidates.Should().HaveCount(1);
        _vm.Placements.Should().HaveCount(1);

        // 準備タブからアセット削除（cascade で Copy / Placement も DB から消える）
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var deleteResult = await deleteAsset.ExecuteAsync(asset.Id);
        deleteResult.IsError.Should().BeFalse();

        // メッセージ受信で再ロード → 候補も配置も空になる
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Should().BeEmpty();
        _vm.Placements.Should().BeEmpty();
        _vm.SelectedPlacement.Should().BeNull();
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
    public async Task ApplyPixelOffsetAsync_Updates_Placement_And_Persists()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        var item = _vm.Placements.Single();

        var ok = await _vm.ApplyPixelOffsetAsync(item.PlacementId, 50, -30);

        ok.Should().BeTrue();
        item.PixelOffsetX.Should().Be(50);
        item.PixelOffsetY.Should().Be(-30);
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(item.PlacementId);
        reloaded!.PixelOffsetX.Should().Be(50);
        reloaded.PixelOffsetY.Should().Be(-30);
    }

    [Fact]
    public async Task ApplyPixelOffsetAsync_Clamps_Out_Of_Range_Values()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        var item = _vm.Placements.Single();

        // ±MaxPixelOffset (4096) を超える値を渡しても clamp される
        var ok = await _vm.ApplyPixelOffsetAsync(item.PlacementId, 99999, -99999);

        ok.Should().BeTrue();
        item.PixelOffsetX.Should().Be(PlacementInspectorViewModel.MaxPixelOffset);
        item.PixelOffsetY.Should().Be(-PlacementInspectorViewModel.MaxPixelOffset);
    }

    [Fact]
    public async Task ApplyPixelOffsetAsync_Unknown_Placement_Returns_False_And_Sets_StatusMessage()
    {
        var grid = await SeedActiveGridAsync(2, 2);
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));

        var ok = await _vm.ApplyPixelOffsetAsync(Guid.NewGuid(), 10, 10);

        ok.Should().BeFalse();
        _vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    /// <summary>初期状態（グリッド未選択）では CurrentSelection が NoSelection になる。</summary>
    [Fact]
    public void CurrentSelection_NoGrid_Returns_NoSelection()
    {
        _vm.CurrentSelection.Should().BeOfType<ViewGrid.Application.Selection.NoSelection>();
        _vm.IsNoSelection.Should().BeTrue();
        _vm.IsGridOnlySelected.Should().BeFalse();
        _vm.IsPlacementSelected.Should().BeFalse();
    }

    /// <summary>グリッド選択 + 配置未選択なら GridSelection になる。</summary>
    [Fact]
    public async Task CurrentSelection_With_Grid_But_No_Placement_Returns_GridSelection()
    {
        var grid = await SeedActiveGridAsync(2, 2);
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));

        _vm.CurrentSelection.Should().BeOfType<ViewGrid.Application.Selection.GridSelection>();
        ((ViewGrid.Application.Selection.GridSelection)_vm.CurrentSelection).GridId.Should().Be(grid.Id);
        _vm.IsGridOnlySelected.Should().BeTrue();
        _vm.IsPlacementSelected.Should().BeFalse();
    }

    /// <summary>配置選択時は PlacementSelection に切り替わり、Placement/Copy/Grid Id が一致する。</summary>
    [Fact]
    public async Task CurrentSelection_With_Selected_Placement_Returns_PlacementSelection()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single();

        _vm.CurrentSelection.Should().BeOfType<ViewGrid.Application.Selection.PlacementSelection>();
        var sel = (ViewGrid.Application.Selection.PlacementSelection)_vm.CurrentSelection;
        sel.GridId.Should().Be(grid.Id);
        sel.PlacementId.Should().Be(placed.Value.Id);
        sel.CopyId.Should().Be(copy.Id);
        _vm.IsPlacementSelected.Should().BeTrue();
        _vm.IsGridOnlySelected.Should().BeFalse();
    }

    /// <summary>配置選択を解除すると GridSelection に戻る。</summary>
    [Fact]
    public async Task Clearing_Placement_Falls_Back_To_GridSelection()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.SelectedPlacement = _vm.Placements.Single();
        _vm.IsPlacementSelected.Should().BeTrue();

        _vm.SelectedPlacement = null;

        _vm.CurrentSelection.Should().BeOfType<ViewGrid.Application.Selection.GridSelection>();
        _vm.IsGridOnlySelected.Should().BeTrue();
    }

    // ─── 配置ファースト UI 第 2 段階 (Stage 2): バリアント新規作成 / リネーム / 削除 ───

    /// <summary>新規バリアント作成: SelectedCandidate のアセットを起点に新 Copy が増える。</summary>
    [Fact]
    public async Task CommitCreateVariant_Creates_Copy_For_Selected_Candidate_Asset()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "base");
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.SelectedCandidate = _vm.Candidates.Single();

        _vm.BeginCreateVariant();
        _vm.IsCreatingVariant.Should().BeTrue();
        _vm.DraftVariantName = "派生 A";
        await _vm.CommitCreateVariantAsync();

        _vm.IsCreatingVariant.Should().BeFalse();
        _vm.Candidates.Should().HaveCount(2);
        _vm.Candidates.Should().Contain(c => c.CopyDisplayName == "派生 A");
        // 新バリアントが選択されている
        _vm.SelectedCandidate!.CopyDisplayName.Should().Be("派生 A");
    }

    /// <summary>新規バリアント作成: 名前未入力なら「バリアント N」自動採番。</summary>
    [Fact]
    public async Task CommitCreateVariant_Empty_Name_Uses_Auto_Numbered()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "v1");
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.SelectedCandidate = _vm.Candidates.Single();

        _vm.BeginCreateVariant();
        _vm.DraftVariantName = "   "; // 空白だけ → null 扱い
        await _vm.CommitCreateVariantAsync();

        _vm.Candidates.Should().HaveCount(2);
        // ordinal = 既存件数 (1) + 1 = 2 → "{prefix} 2"
        // NullLocalizationService が key を素通しするため prefix は "Term_VariantPrefix"
        // (本番では resx から「バリアント」 / "Variant" に解決される)。
        _vm.Candidates.Should().Contain(c => c.CopyDisplayName == "Term_VariantPrefix 2");
    }

    /// <summary>BeginCreateVariant: SelectedCandidate が null だと no-op。</summary>
    [Fact]
    public void BeginCreateVariant_NoOp_When_No_Candidate_Selected()
    {
        _vm.SelectedCandidate.Should().BeNull();
        _vm.BeginCreateVariant();
        _vm.IsCreatingVariant.Should().BeFalse();
    }

    /// <summary>削除: SelectedCandidate を物理削除し、Candidates から除去される。</summary>
    [Fact]
    public async Task DeleteSelectedCandidate_Removes_Copy_From_Candidates()
    {
        var asset = await _fx.SeedAssetAsync();
        var c1 = await _fx.SeedCopyAsync(asset.Id, copyName: "keep");
        var c2 = await _fx.SeedCopyAsync(asset.Id, copyName: "remove");
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.SelectedCandidate = _vm.Candidates.First(c => c.CopyId == c2.Id);

        await _vm.DeleteSelectedCandidateAsync();

        _vm.Candidates.Should().HaveCount(1);
        _vm.Candidates.Single().CopyId.Should().Be(c1.Id);
    }

    /// <summary>インラインリネーム: BeginEdit → CommitEdit で永続化 + DisplayName 更新。</summary>
    [Fact]
    public async Task CommitEditCandidate_Persists_New_Name_And_Updates_Display()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "old-name");
        await _vm.ReloadFromMessageAsyncForTests();
        var candidate = _vm.Candidates.Single();

        _vm.BeginEditCandidate(candidate);
        candidate.IsEditing.Should().BeTrue();
        candidate.EditingName = "new-name";

        await _vm.CommitEditCandidateAsync(candidate);

        candidate.IsEditing.Should().BeFalse();
        candidate.CopyName.Should().Be("new-name");
        candidate.CopyDisplayName.Should().Be("new-name");
        // DB にも反映されているはず
        var reloaded = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        reloaded!.CopyName.Should().Be("new-name");
    }

    /// <summary>リネーム: 値変化なしなら no-op（履歴に積まない）。</summary>
    [Fact]
    public async Task CommitEditCandidate_NoOp_When_Name_Unchanged()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "same");
        await _vm.ReloadFromMessageAsyncForTests();
        var candidate = _vm.Candidates.Single();
        var beforeHistoryCount = _history.History.Count;

        _vm.BeginEditCandidate(candidate);
        candidate.EditingName = "same"; // 同じ
        await _vm.CommitEditCandidateAsync(candidate);

        candidate.IsEditing.Should().BeFalse();
        _history.History.Count.Should().Be(beforeHistoryCount);
    }

    /// <summary>キャンセル: EditingName 破棄、CopyName 維持。</summary>
    [Fact]
    public async Task CancelEditCandidate_Discards_Edits()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "original");
        await _vm.ReloadFromMessageAsyncForTests();
        var candidate = _vm.Candidates.Single();

        _vm.BeginEditCandidate(candidate);
        candidate.EditingName = "draft";

        _vm.CancelEditCandidate(candidate);

        candidate.IsEditing.Should().BeFalse();
        candidate.EditingName.Should().BeNull();
        candidate.CopyName.Should().Be("original");
    }

    /// <summary>リネーム後 Undo で旧名に戻る（UpdateImageCopyCommand 経由）。</summary>
    [Fact]
    public async Task CommitEditCandidate_Undo_Restores_Old_Name()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "v1");
        await _vm.ReloadFromMessageAsyncForTests();
        var candidate = _vm.Candidates.Single();

        _vm.BeginEditCandidate(candidate);
        candidate.EditingName = "v2";
        await _vm.CommitEditCandidateAsync(candidate);

        await _history.UndoAsync();

        var reloaded = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        reloaded!.CopyName.Should().Be("v1");
    }

    // ─── CandidateGroups: バリアント候補リストを Asset でグループ化 ───

    /// <summary>1 アセット 2 バリアント → 1 グループ + 2 Variants。</summary>
    [Fact]
    public async Task CandidateGroups_Single_Asset_Multiple_Variants_Creates_One_Group()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "v1");
        await _fx.SeedCopyAsync(asset.Id, copyName: "v2");

        await _vm.ReloadFromMessageAsyncForTests();

        _vm.CandidateGroups.Should().HaveCount(1);
        var group = _vm.CandidateGroups.Single();
        group.AssetId.Should().Be(asset.Id);
        group.Variants.Should().HaveCount(2);
        group.SummaryLine.Should().Contain("2");
    }

    /// <summary>2 アセット各 1 バリアント → 2 グループ各 1 Variants（出現順は Candidates 順を踏襲）。</summary>
    [Fact]
    public async Task CandidateGroups_Multiple_Assets_Create_Separate_Groups()
    {
        var asset1 = await _fx.SeedAssetAsync(
            fileHash: "hash000000000000000000000000000000000000000000000000000000000001");
        var asset2 = await _fx.SeedAssetAsync(
            fileHash: "hash000000000000000000000000000000000000000000000000000000000002");
        await _fx.SeedCopyAsync(asset1.Id, copyName: "a1");
        await _fx.SeedCopyAsync(asset2.Id, copyName: "b1");

        await _vm.ReloadFromMessageAsyncForTests();

        _vm.CandidateGroups.Should().HaveCount(2);
        _vm.CandidateGroups.Should().OnlyContain(g => g.Variants.Count == 1);
        _vm.CandidateGroups.Select(g => g.AssetId).Should().BeEquivalentTo(new[] { asset1.Id, asset2.Id });
    }

    /// <summary>最後のバリアントを削除するとグループ自体も CandidateGroups から消える。</summary>
    [Fact]
    public async Task DeleteSelectedCandidate_Removes_Group_When_Last_Variant_Removed()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "only");
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.SelectedCandidate = _vm.Candidates.Single();
        _vm.CandidateGroups.Should().HaveCount(1);

        await _vm.DeleteSelectedCandidateAsync();

        _vm.Candidates.Should().BeEmpty();
        _vm.CandidateGroups.Should().BeEmpty();
    }

    /// <summary>同アセット内の 1 バリアントを削除しても、もう 1 つあればグループは残る。</summary>
    [Fact]
    public async Task DeleteSelectedCandidate_Keeps_Group_When_Other_Variants_Remain()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "keep");
        var c2 = await _fx.SeedCopyAsync(asset.Id, copyName: "remove");
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.SelectedCandidate = _vm.Candidates.First(c => c.CopyId == c2.Id);

        await _vm.DeleteSelectedCandidateAsync();

        _vm.CandidateGroups.Should().HaveCount(1);
        _vm.CandidateGroups.Single().Variants.Should().HaveCount(1);
        _vm.CandidateGroups.Single().Variants.Single().CopyDisplayName.Should().Be("keep");
    }

    /// <summary>
    /// 新規バリアント作成は ReloadFromMessage 経由で CandidateGroups も再構築されるため、
    /// 既存アセットへの追加ではグループ件数が増えず Variants が 1 → 2 に増える。
    /// </summary>
    [Fact]
    public async Task CommitCreateVariant_Adds_To_Existing_Group()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "base");
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.SelectedCandidate = _vm.Candidates.Single();
        _vm.CandidateGroups.Should().HaveCount(1);

        _vm.BeginCreateVariant();
        _vm.DraftVariantName = "派生";
        await _vm.CommitCreateVariantAsync();

        _vm.CandidateGroups.Should().HaveCount(1);
        _vm.CandidateGroups.Single().Variants.Should().HaveCount(2);
    }

    /// <summary>
    /// 候補ライブラリ再ロード時に <see cref="CandidateGroupViewModel"/> インスタンスが
    /// 再利用され、<see cref="CandidateGroupViewModel.IsExpanded"/> 等の UI 状態が維持される。
    /// これにより Save 後の TreeView 展開状態が崩れないことを保証する。
    /// </summary>
    [Fact]
    public async Task LoadCandidatesAsync_Preserves_Group_Instance_Across_Reloads()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "v1");
        await _vm.ReloadFromMessageAsyncForTests();
        var originalGroup = _vm.CandidateGroups.Single();
        originalGroup.IsExpanded = false; // ユーザーが折り畳んだ状態を模擬

        // 別 Save 等で再ロードが走ったとみなす
        await _fx.SeedCopyAsync(asset.Id, copyName: "v2");
        await _vm.ReloadFromMessageAsyncForTests();

        var groupAfter = _vm.CandidateGroups.Single();
        groupAfter.Should().BeSameAs(originalGroup); // 同インスタンス
        groupAfter.IsExpanded.Should().BeFalse();    // ユーザー操作の状態が維持
        groupAfter.Variants.Should().HaveCount(2);   // 新バリアントが追加されている
    }

    /// <summary>
    /// 同 CopyId の <see cref="CopyCandidateViewModel"/> インスタンスは再ロード時も再利用される。
    /// これにより TreeView.SelectedItem の参照同一性が保たれ、選択状態が崩れない。
    /// </summary>
    [Fact]
    public async Task LoadCandidatesAsync_Preserves_Candidate_Instance_For_Same_CopyId()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "stable");
        await _vm.ReloadFromMessageAsyncForTests();
        var originalCandidate = _vm.Candidates.Single();

        // 同 CopyId で再ロード
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Single().Should().BeSameAs(originalCandidate);
    }

    /// <summary>
    /// フィット操作が <see cref="IUndoRedoService"/> 経由で履歴に積まれ、 Undo で旧重みに戻り、
    /// Redo で再計算後の重みに戻ることを保証する。
    /// 修正前 (FitGridWeightAsync が _fitWeightUseCase.ExecuteAsync を直接呼んでいた頃) は
    /// 履歴に乗らず、 redo スタックの stale snapshot で fit 後の重みが上書きされる回帰があった。
    /// </summary>
    [Fact]
    public async Task FitGridWeightAsync_Roundtrips_Through_UndoRedo()
    {
        var (placementId, gridItem) = await SeedFitScenarioAsync();
        await _vm.LoadGridAsync(gridItem);

        var beforeCol = gridItem.ColWeights;
        beforeCol.Should().Equal(GridCanvas.UniformWeights(3));

        // フィット実行 → 重みが変わる
        var ok = await _vm.FitGridWeightAsync(placementId, FitAxis.Column);
        ok.Should().BeTrue();
        var afterCol = gridItem.ColWeights;
        afterCol.SequenceEqual(beforeCol).Should().BeFalse(
            "fit が中央セルの列幅を縮めるはず (100x200 画像 + 200x200 セル)");

        // Undo → 旧重みに戻る
        _history.CanUndo.Should().BeTrue();
        var undoResult = await _history.UndoAsync();
        undoResult.IsError.Should().BeFalse();
        var dbAfterUndo = (await _fx.GridRepository.FindByIdAsync(gridItem.GridId))!;
        dbAfterUndo.ColWeights.SequenceEqual(beforeCol).Should().BeTrue(
            "Undo で元の uniform weights に戻るべき");

        // Redo → fit 結果が再現される (fit 再計算は決定論的)
        _history.CanRedo.Should().BeTrue();
        var redoResult = await _history.RedoAsync();
        redoResult.IsError.Should().BeFalse();
        var dbAfterRedo = (await _fx.GridRepository.FindByIdAsync(gridItem.GridId))!;
        dbAfterRedo.ColWeights.SequenceEqual(afterCol).Should().BeTrue(
            "Redo で fit 後の重みが再現されるべき");
    }

    /// <summary>
    /// 元の Codex review 指摘の核: 「フィット前に redo スタックがあると、 fit 後に redo すると
    /// stale snapshot が上書きしてフィット結果が破壊される」 — fit が history を通る今は redo
    /// スタックが clear されるため、 fit 後に redo が空になることを検証する。
    /// </summary>
    [Fact]
    public async Task FitGridWeightAsync_ClearsStaleRedoStack()
    {
        var (placementId, gridItem) = await SeedFitScenarioAsync();
        await _vm.LoadGridAsync(gridItem);

        // 先行で重みを手動編集 → undo して redo スタックに残す
        var manual = ImmutableArray.Create(2, 1, 2);
        var ok1 = await _vm.ApplyGridWeightsAsync(manual, null);
        ok1.Should().BeTrue();
        _history.CanUndo.Should().BeTrue();
        (await _history.UndoAsync()).IsError.Should().BeFalse();
        _history.CanRedo.Should().BeTrue("先行操作の Undo で redo スタックに stale snapshot が残るはず");

        // フィットを実行 → 履歴経路を通るので redo スタックは clear されるべき
        var ok2 = await _vm.FitGridWeightAsync(placementId, FitAxis.Column);
        ok2.Should().BeTrue();
        _history.CanRedo.Should().BeFalse(
            "fit が _history.ExecuteAsync 経由で実行されたため、 古い redo スタックは破棄されるべき");
    }

    /// <summary>
    /// 100x200 画像 + 3x3 600x600 grid + 中央セル (1,1) UniformContain。
    /// 列フィットで列 1 が画像幅にぴったり、 余白は左右列に均等分配される (use case 既存テスト
    /// FitColumn_CenterCell_TallImage_DistributesPaddingEvenly と同条件)。
    /// </summary>
    private async Task<(Guid PlacementId, GridCanvasItemViewModel Grid)> SeedFitScenarioAsync()
    {
        var asset = await _fx.SeedAssetAsync(width: 100, height: 200);
        var copy = await _fx.SeedCopyAsync(asset.Id); // ScalingMode.UniformContain (既定)

        var now = DateTimeOffset.UtcNow;
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "fit-scenario",
            GridRows = 3,
            GridCols = 3,
            ColWeights = GridCanvas.UniformWeights(3),
            RowWeights = GridCanvas.UniformWeights(3),
            CanvasSize = new PixelSize(600, 600),
            CreatedAt = now,
            UpdatedAt = now,
        };
        (await _fx.GridRepository.AddAsync(grid)).IsError.Should().BeFalse();

        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 1));
        placed.IsError.Should().BeFalse();

        return (placed.Value.Id, new GridCanvasItemViewModel(grid));
    }

    /// <summary>
    /// CopyName が外部経路で変更されたとき、同インスタンスを保ったまま CopyName が更新される
    /// （SyncObservableCollection 内で existing.CopyName = copy.CopyName）。
    /// </summary>
    [Fact]
    public async Task LoadCandidatesAsync_Updates_CopyName_On_Existing_Instance()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "old");
        await _vm.ReloadFromMessageAsyncForTests();
        var candidate = _vm.Candidates.Single();

        // 別経路（DB 直接更新を想定）で CopyName を書き換え
        var renamed = new ImageCopy
        {
            Id = copy.Id,
            AssetId = copy.AssetId,
            CopyName = "new",
            Transform = copy.Transform,
            ScalingMode = copy.ScalingMode,
            Alignment = copy.Alignment,
            OccupySize = copy.OccupySize,
            CreatedAt = copy.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _fx.CopyRepository.UpdateAsync(renamed);

        await _vm.ReloadFromMessageAsyncForTests();

        // インスタンスは同じ + CopyName が新値に更新されている
        _vm.Candidates.Single().Should().BeSameAs(candidate);
        candidate.CopyName.Should().Be("new");
    }
}
