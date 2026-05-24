using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.AutoSave;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Selection;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブのワークスペース。アクティブグリッド、配置済み一覧、配置候補を保持し、
/// 配置/取消コマンドを提供する。
/// </summary>
public sealed partial class GridWorkspaceViewModel : ViewModelBase, IRecipient<CopyLibraryChangedMessage>, IGridOutputContext, IVariantManagerContext, IGridStructureEditorContext, IDisposable
{
    private readonly IGridCanvasRepository _gridRepository;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IImageCropResolver _cropResolver;
    private readonly PlaceImageCopyUseCase _placeUseCase;
    private readonly RemovePlacementUseCase _removeUseCase;
    private readonly MovePlacementUseCase _moveUseCase;
    private readonly SwapPlacementsUseCase _swapUseCase;
    // RenderGridUseCase / ExportGridUseCase は GridOutputViewModel へ移管 (Phase 4)。
    // コンストラクタ引数はまだ受け取って Output 構築時に渡すが、 本 VM 自身では保持しない。
    // UpdateGridWeightsUseCase / UpdateGridLocksUseCase / FitGridWeightToPlacementUseCase は
    // GridStructureEditorViewModel へ移管 (Phase 4-3)。 引数のみ受け取って Structure 構築時に渡す。
    private readonly UpdatePlacementOffsetUseCase _updateOffsetUseCase;
    // CreateLogicalCopyUseCase / UpdateImageCopyUseCase / DeleteImageAssetUseCase は
    // VariantManagerViewModel へ移管 (Phase 4-2)。 引数のみ受け取って Variants 構築時に渡す。
    private readonly IImageStorage _imageStorage;
    private readonly IAppSettingsService _appSettings;
    private readonly SaveCoordinator _variantAutoSave;
    // IFilePickerService は GridOutputViewModel へ移管 (Phase 4)。 引数のみ受け取って Output に渡す。
    private readonly IMessenger _messenger;
    private readonly IUndoRedoService _history;
    private readonly ILocalizationService _loc;
    private readonly ILogger<GridWorkspaceViewModel> _logger;

    public PlacementInspectorViewModel Inspector { get; }

    /// <summary>
    /// 候補リストでバリアントを選択中 (= 配置未選択) に右ペインに表示する共有特性編集 VM。
    /// PlacementInspector 内の CopyProperties とは別インスタンス。
    /// 配置選択中は Inspector.CopyProperties が使われるのでこちらは表示されない。
    /// </summary>
    public CopyPropertiesViewModel VariantProperties { get; }

    [ObservableProperty]
    public partial GridCanvasItemViewModel? CurrentGrid { get; set; }

    [ObservableProperty]
    public partial CopyCandidateViewModel? SelectedCandidate { get; set; }

    [ObservableProperty]
    public partial PlacementItemViewModel? SelectedPlacement { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>
    /// 出力 / プレビューパネル (右カラム下) の子 VM。 OutputMode / TrimMode / PhotoBoardStyle や
    /// プレビュー生成・PNG エクスポートはすべて <see cref="GridOutputViewModel"/> に委譲する。
    /// View binding は <c>{Binding Output.SelectedOutputMode}</c> のように Output 経由でアクセス。
    /// </summary>
    public GridOutputViewModel Output { get; }

    /// <summary>
    /// バリアント新規作成 / 削除 / インラインリネームを司る子 VM。 Phase 4 で抽出。
    /// View binding は <c>{Binding Variants.BeginCreateVariantCommand}</c> のように Variants 経由で。
    /// IsCreatingVariant / DraftVariantName も <see cref="VariantManagerViewModel"/> 側に移管済み。
    /// </summary>
    public VariantManagerViewModel Variants { get; }

    /// <summary>
    /// グリッド構造編集 (列・行の重み更新 / ロック / FitGridWeight) を司る子 VM。 Phase 4 で抽出。
    /// 公開メソッド (<see cref="ApplyGridWeightsAsync"/> / <see cref="FitGridWeightAsync"/> /
    /// <see cref="ToggleColLockAsync"/> / <see cref="ToggleRowLockAsync"/>) は本 VM の薄い wrapper を
    /// 残置して View / code-behind の経路を不変に保つ (Phase 4-3 で後方互換のためのファサード)。
    /// </summary>
    public GridStructureEditorViewModel Structure { get; }

    // IsCreatingVariant / DraftVariantName は VariantManagerViewModel へ移管 (Phase 4-2)。
    // View からは {Binding Variants.IsCreatingVariant} / {Binding Variants.DraftVariantName} で参照。


    public ObservableCollection<PlacementItemViewModel> Placements { get; } = [];
    public ObservableCollection<CopyCandidateViewModel> Candidates { get; } = [];

    /// <summary>
    /// <see cref="Candidates"/> を Asset 単位でまとめたグループ表示用コレクション。
    /// View 側は TreeView の <c>ItemsSource</c> としてこの collection を使う。
    /// <see cref="LoadCandidatesAsync"/> および <see cref="DeleteSelectedCandidateAsync"/> 等の
    /// 個別操作の中で Candidates と二重管理される（Candidates は VM 内ロジック互換のため残置）。
    /// </summary>
    public ObservableCollection<CandidateGroupViewModel> CandidateGroups { get; } = [];

    public bool HasGrid => CurrentGrid is not null;

    /// <summary>
    /// 配置タブの右ペイン下段（プロパティ領域）が「いま誰のプロパティを表示すべきか」の文脈。
    /// <see cref="CurrentGrid"/> / <see cref="SelectedPlacement"/> の組合せで派生し、
    /// View 側は <c>ContentControl</c> + <c>DataTemplates</c> でこの値の型に応じて
    /// テンプレート（Inspector / GridProperties / 空状態案内）を出し分ける。
    /// </summary>
    public ISelectionContext CurrentSelection
    {
        get
        {
            // 優先順位: Placement > Variant > Grid > None。
            // 配置が選択されていればそれが最強 (= 共有特性編集は Inspector 内の CopyProperties で行う)。
            // 配置がなくバリアントが選択中なら VariantSelection (= スタンドアロン CopyProperties を表示)。
            if (CurrentGrid is { } grid && SelectedPlacement is { } p)
                return new PlacementSelection(grid.GridId, p.PlacementId, p.CopyId);
            if (SelectedCandidate is { } cand)
                return new VariantSelection(cand.CopyId, cand.AssetId);
            if (CurrentGrid is { } gridOnly)
                return new GridSelection(gridOnly.GridId);
            return NoSelection.Instance;
        }
    }

    /// <summary>View 側で <c>IsVisible</c> バインドを使う場合の利便プロパティ（DataTemplate 切替なら不要）。</summary>
    public bool IsPlacementSelected => CurrentSelection is PlacementSelection;
    public bool IsVariantSelected => CurrentSelection is VariantSelection;
    public bool IsGridOnlySelected => CurrentSelection is GridSelection;
    public bool IsNoSelection => CurrentSelection is NoSelection;

    public GridWorkspaceViewModel(
        IGridCanvasRepository gridRepository,
        IImageCopyRepository copyRepository,
        IImageAssetRepository assetRepository,
        IGridPlacementRepository placementRepository,
        IThumbnailService thumbnailService,
        IImageCropResolver cropResolver,
        PlaceImageCopyUseCase placeUseCase,
        RemovePlacementUseCase removeUseCase,
        MovePlacementUseCase moveUseCase,
        SwapPlacementsUseCase swapUseCase,
        RenderGridUseCase renderUseCase,
        ExportGridUseCase exportUseCase,
        UpdateGridWeightsUseCase updateWeightsUseCase,
        UpdateGridLocksUseCase updateLocksUseCase,
        UpdatePlacementOffsetUseCase updateOffsetUseCase,
        FitGridWeightToPlacementUseCase fitWeightUseCase,
        CreateLogicalCopyUseCase createCopyUseCase,
        UpdateImageCopyUseCase updateCopyUseCase,
        DeleteImageAssetUseCase deleteAssetUseCase,
        IImageStorage imageStorage,
        IAppSettingsService appSettings,
        IFilePickerService filePicker,
        IMessenger messenger,
        IUndoRedoService history,
        PlacementInspectorViewModel inspector,
        CopyPropertiesViewModel variantProperties,
        ILocalizationService loc,
        ILogger<GridWorkspaceViewModel> logger)
    {
        _gridRepository = gridRepository;
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _placementRepository = placementRepository;
        _thumbnailService = thumbnailService;
        _cropResolver = cropResolver;
        _placeUseCase = placeUseCase;
        _removeUseCase = removeUseCase;
        _moveUseCase = moveUseCase;
        _swapUseCase = swapUseCase;
        // renderUseCase / exportUseCase は Output に渡すだけで保持しない (Phase 4 移管)
        // updateWeightsUseCase / updateLocksUseCase / fitWeightUseCase は Structure に渡すだけ (Phase 4-3 移管)
        _updateOffsetUseCase = updateOffsetUseCase;
        // createCopyUseCase / updateCopyUseCase / deleteAssetUseCase は Variants に渡すだけ (Phase 4-2 移管)
        _imageStorage = imageStorage;
        _appSettings = appSettings;
        // filePicker は Output に渡すだけで保持しない (Phase 4 移管)
        _messenger = messenger;
        _history = history;
        Inspector = inspector;
        VariantProperties = variantProperties;
        _loc = loc;
        _logger = logger;

        // 出力 / プレビューパネルの子 VM を構築。 this を IGridOutputContext として渡して
        // CurrentGrid / IsBusy / StatusMessage を共有させる (循環参照ではなく親子委譲)。
        Output = new GridOutputViewModel(this, renderUseCase, exportUseCase, filePicker, loc, logger);

        // バリアント管理 (新規作成 / 削除 / リネーム) の子 VM を構築。 同じく this を
        // IVariantManagerContext として渡し、 候補リスト (Candidates / CandidateGroups /
        // SelectedCandidate) と IsBusy / StatusMessage / LoadCandidatesAsync を共有させる。
        Variants = new VariantManagerViewModel(
            this, createCopyUseCase, updateCopyUseCase, deleteAssetUseCase,
            copyRepository, history, messenger, loc, logger);

        // グリッド構造編集 (重み / ロック / FitGridWeight) の子 VM を構築。 this を
        // IGridStructureEditorContext として渡し、 CurrentGrid 参照 + StatusMessage 書込 +
        // NotifyCurrentGridChanged (= OnPropertyChanged(nameof(CurrentGrid))) を共有させる。
        Structure = new GridStructureEditorViewModel(
            this, gridRepository, updateWeightsUseCase, updateLocksUseCase, fitWeightUseCase, history, loc);

        // VariantProperties (= 候補リスト経由のスタンドアロン共有特性編集) は
        // PlacementInspector 経由のものとは別経路なので、 専用 SaveCoordinator で
        // auto-save を駆動する。 PlacementInspector 側の auto-save 配線とは独立。
        _variantAutoSave = new SaveCoordinator(
            TimeSpan.FromMilliseconds(1000),
            isEnabled: () => _appSettings.Current.EnableAutoSave,
            isDirty: () => VariantProperties.IsDirty && VariantProperties.HasCopy,
            signatureProvider: ComputeVariantAutoSaveSignature,
            saveAction: ct => VariantProperties.TrySaveAsync(ct));
        VariantProperties.PropertyChanged += OnVariantPropertiesChanged;
        _appSettings.Changed += OnAppSettingsChangedForVariant;

        // ライブプレビュー配線: VariantProperties / Inspector.CopyProperties の draft 変更を
        // 該当 CopyId の placement VM (PlacementItemViewModel) に即時 push し、 attach 切替や
        // Revert で draft が破棄される際は DB から ApplyCopyChanges で rollback する。
        Inspector.CopyProperties.PropertyChanged += OnInspectorCopyPropertiesChanged;
        VariantProperties.DraftReverted += OnVariantPropertiesDraftReverted;
        Inspector.CopyProperties.DraftReverted += OnInspectorCopyPropertiesDraftReverted;

        _messenger.Register(this);
    }

    /// <summary>
    /// VariantProperties の編集系プロパティ変化を auto-save coordinator に通知 + ライブプレビュー push。
    /// 内部状態の通知も含まれるが NotifyEdited は dirty / 設定 OFF / 失敗 retry を gate してくれる。
    /// </summary>
    private void OnVariantPropertiesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _variantAutoSave.NotifyEdited();
        HandleCopyPropertiesEvent(VariantProperties, ref _variantPropertiesLastCopyId, e.PropertyName);
    }

    /// <summary>
    /// Inspector embed の CopyProperties の編集系プロパティ変化を捕まえてライブプレビュー push する。
    /// auto-save 連動は Inspector 側で既に行われているのでここでは push と attach 切替検知のみ担当。
    /// </summary>
    private void OnInspectorCopyPropertiesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        HandleCopyPropertiesEvent(Inspector.CopyProperties, ref _inspectorCopyPropertiesLastCopyId, e.PropertyName);
    }

    /// <summary>
    /// CopyProperties (VariantProperties / Inspector.CopyProperties 共通) の PropertyChanged ハンドリング。
    /// (1) attach 切替の検知: 直前の CopyId と比較して変化があれば、 旧 CopyId の placement を DB から rollback、
    /// (2) ライブプレビュー push: <see cref="IsLivePreviewSharedProperty"/> に該当するなら現在 attach 中の
    /// CopyId を使う placement に値を伝播。 IsAttaching 中は内部更新なので push しない (DB 値 = 現在値で no-op)。
    /// </summary>
    private void HandleCopyPropertiesEvent(
        CopyPropertiesViewModel cp,
        ref Guid? lastCopyId,
        string? propertyName)
    {
        var currentCopyId = cp.AttachedCopyId;
        if (currentCopyId != lastCopyId)
        {
            var previous = lastCopyId;
            lastCopyId = currentCopyId;
            if (previous is Guid old)
                _ = RollbackPlacementsForCopyAsync(old, CancellationToken.None);
        }

        if (cp.IsAttaching) return;
        if (!IsLivePreviewSharedProperty(propertyName)) return;
        if (currentCopyId is not Guid copyId) return;
        PushSharedPropertyLivePreview(cp, copyId, propertyName);
    }

    private void OnVariantPropertiesDraftReverted(object? sender, Guid copyId)
        => _ = RollbackPlacementsForCopyAsync(copyId, CancellationToken.None, skipIfStillAttached: false);

    private void OnInspectorCopyPropertiesDraftReverted(object? sender, Guid copyId)
        => _ = RollbackPlacementsForCopyAsync(copyId, CancellationToken.None, skipIfStillAttached: false);

    /// <summary>
    /// <see cref="CopyPropertiesViewModel"/> の編集バッファのうち、 canvas 表示に即時反映できる軽量な
    /// 共有プロパティかどうか。 AutoCrop / ManualCrop / ProtectedRegions は Phase 2 以降。
    /// </summary>
    private static bool IsLivePreviewSharedProperty(string? propertyName) => propertyName is
        nameof(CopyPropertiesViewModel.Rotation) or
        nameof(CopyPropertiesViewModel.FlipX) or
        nameof(CopyPropertiesViewModel.FlipY) or
        nameof(CopyPropertiesViewModel.ScalingMode) or
        nameof(CopyPropertiesViewModel.AlignX) or
        nameof(CopyPropertiesViewModel.AlignY);

    /// <summary>
    /// draft 中の共有プロパティ値を、 当該 CopyId を使う全 <see cref="PlacementItemViewModel"/> に push する。
    /// AlignX / AlignY は <see cref="Alignment"/> 構造体を再構築して 1 setter で渡す (個別 setter がないため)。
    /// 同値書込は ObservableProperty の等価性チェックで no-op。
    /// </summary>
    private void PushSharedPropertyLivePreview(CopyPropertiesViewModel cp, Guid copyId, string? propertyName)
    {
        foreach (var p in Placements)
        {
            if (p.CopyId != copyId) continue;
            switch (propertyName)
            {
                case nameof(CopyPropertiesViewModel.Rotation):
                    p.Rotation = cp.Rotation;
                    break;
                case nameof(CopyPropertiesViewModel.FlipX):
                    p.FlipX = cp.FlipX;
                    break;
                case nameof(CopyPropertiesViewModel.FlipY):
                    p.FlipY = cp.FlipY;
                    break;
                case nameof(CopyPropertiesViewModel.ScalingMode):
                    p.ScalingMode = cp.ScalingMode;
                    break;
                case nameof(CopyPropertiesViewModel.AlignX):
                case nameof(CopyPropertiesViewModel.AlignY):
                    p.Alignment = new Alignment(cp.AlignX, cp.AlignY);
                    break;
            }
        }
    }

    /// <summary>
    /// 指定 CopyId を使う全 <see cref="PlacementItemViewModel"/> を DB の最新 <see cref="ImageCopy"/> 値に
    /// 巻き戻す。 ライブプレビューで push した未保存値を canvas から取り除くために、 attach 切替や Revert
    /// のタイミングで呼ばれる。 DB 読込失敗は黙って続行 (次の <see cref="LoadPlacementsAsync"/> で整合される)。
    /// <para>
    /// <paramref name="skipIfStillAttached"/> = <c>true</c> (既定): attach 切替検知経由の rollback。
    /// DB await 中にユーザーが当該 copyId に再 attach した場合は draft 値を上書きしないようスキップ
    /// (Codex review 指摘の race fix: A→B→A→edit の高速操作で古い rollback が新しい draft を消す問題)。
    /// <paramref name="skipIfStillAttached"/> = <c>false</c>: <c>Revert</c> 経由の明示的 discard。
    /// 同 source への attach は維持されたままだが ユーザー intent は「DB 値に戻す」なので必ず apply。
    /// </para>
    /// </summary>
    private async Task RollbackPlacementsForCopyAsync(
        Guid copyId, CancellationToken ct, bool skipIfStillAttached = true)
    {
        try
        {
            var copy = await _copyRepository.FindByIdAsync(copyId, ct);
            if (copy is null) return;
            if (skipIfStillAttached &&
                (Inspector.CopyProperties.AttachedCopyId == copyId ||
                 VariantProperties.AttachedCopyId == copyId))
                return;
            foreach (var p in Placements)
            {
                if (p.CopyId == copyId)
                    p.ApplyCopyChanges(copy);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* DB エラーは無視 (次の LoadPlacementsAsync で整合される) */ }
    }

    /// <summary>
    /// ライブプレビュー push / rollback で参照する、 各 CopyProperties が直前に attach していた CopyId。
    /// PropertyChanged 観測ごとに <see cref="CopyPropertiesViewModel.AttachedCopyId"/> と比較して、
    /// 変化があれば旧 CopyId の placement を rollback する。 初期値は null。
    /// </summary>
    private Guid? _variantPropertiesLastCopyId;
    private Guid? _inspectorCopyPropertiesLastCopyId;

    private void OnAppSettingsChangedForVariant(object? sender, Core.Settings.AppSettings settings)
    {
        // 設定 OFF にされた瞬間に保留中の auto-save をキャンセル。
        if (!settings.EnableAutoSave) _variantAutoSave.Cancel();
    }

    /// <summary>
    /// VariantProperties 用の auto-save signature。 失敗 retry guard と編集対象切替検出に使う。
    /// CopyId + 主要編集値を連結 (Inspector 側 ComputeAutoSaveSignature と同じ構造)。
    /// </summary>
    private string ComputeVariantAutoSaveSignature()
    {
        var src = VariantProperties.AttachedSourceForTests;
        return string.Concat(
            src?.CopyId.ToString() ?? "null", "|",
            VariantProperties.Rotation, "|",
            VariantProperties.FlipX, "|", VariantProperties.FlipY, "|",
            VariantProperties.ScalingMode, "|",
            VariantProperties.AlignX, "|", VariantProperties.AlignY, "|",
            VariantProperties.AutoCropEnabled, "|",
            VariantProperties.AutoCropPreset, "|",
            VariantProperties.AutoCropThreshold);
    }

    /// <summary>
    /// 直近の placement 切替に伴う Inspector flush + AttachAsync の Task。
    /// 外部 (MainWindowVM 等) が <see cref="WaitPendingInspectorAttachAsync"/> 経由で
    /// 切替の完了を await できる。 OnSelectedPlacementChanged は partial void なので、
    /// Task をフィールドに退避することで「先行の保存が終わってから次の attach」 を保証する。
    /// 連続切替時は各 task が前 task を await してから実行するので、 attach は厳密に直列化される
    /// (= 旧選択への stale attach で UI が古いアイテムに固定される race を防ぐ)。
    /// </summary>
    private Task _pendingInspectorTask = Task.CompletedTask;

    /// <summary>
    /// 配置選択に伴う候補同期 (<see cref="SyncCandidateToPlacement"/>) 実行中フラグ。
    /// この間の <see cref="SelectedCandidate"/> 変更はユーザー操作ではないので、
    /// <see cref="OnSelectedCandidateChanged"/> の「配置選択解除」を抑止する。
    /// </summary>
    private bool _syncingCandidateFromPlacement;

    /// <summary>
    /// SelectedPlacement 変更時に呼ばれる明示 async pipeline。 partial void hook
    /// (<see cref="OnSelectedPlacementChanged"/>) は本メソッドを fire-and-forget で kick するだけ。
    /// テストや外部呼び出しは戻り値の Task を await することで切替完了を待てる。
    /// 連続切替で stale attach が古いアイテムに固定される race は、 _pendingInspectorTask の
    /// task chain (previous task を await してから自分の処理) で吸収する。
    /// </summary>
    public Task SelectPlacementAsync(PlacementItemViewModel? value)
    {
        SyncCandidateToPlacement(value);
        var previous = _pendingInspectorTask;
        _pendingInspectorTask = FlushThenAttachAsync(previous, value, CurrentGrid);
        NotifySelectionChanged();
        return _pendingInspectorTask;
    }

    /// <summary>
    /// 配置選択時に、候補リストの選択をその配置のバリアントへ追従させる。
    /// 配置選択が右ペインで優先される仕様上、追従させないと候補リストのハイライトと
    /// 右ペインの表示内容がズレ、「いまどのバリアントが対象か」が分からなくなる。
    /// 畳まれているグループは展開して選択を可視化する。
    /// <para>
    /// <paramref name="value"/> が null (配置選択解除) のときは候補選択に触れない。
    /// 配置解除はバリアントクリック起点でも起こり (<see cref="OnSelectedCandidateChanged"/>)、
    /// その場合はユーザーが選んだ候補をそのまま残す必要があるため。
    /// </para>
    /// <para>
    /// 同期で生じる <see cref="SelectedCandidate"/> 変更は <see cref="_syncingCandidateFromPlacement"/>
    /// でマークし、<see cref="OnSelectedCandidateChanged"/> 側の「配置選択解除」を抑止する
    /// (この同期は配置選択の付随処理であって、ユーザーのバリアント選択ではない)。
    /// </para>
    /// </summary>
    private void SyncCandidateToPlacement(PlacementItemViewModel? value)
    {
        if (value is null)
            return;

        var match = Candidates.FirstOrDefault(c => c.CopyId == value.CopyId);
        if (match is null || ReferenceEquals(match, SelectedCandidate))
            return;

        var group = CandidateGroups.FirstOrDefault(g => g.AssetId == match.AssetId);
        if (group is not null)
            group.IsExpanded = true;

        _syncingCandidateFromPlacement = true;
        try { SelectedCandidate = match; }
        finally { _syncingCandidateFromPlacement = false; }
    }

    /// <summary>SelectedPlacement 変更時に Inspector を Attach し、CurrentSelection も再計算する。
    /// 描画域サイズの計算に CurrentGrid（重み・キャンバスサイズ）が必要なので渡す。
    /// auto-save ON の場合は AttachAsync の前に Inspector の保留中 auto-save を flush する。
    /// 先行の attach が走行中なら、 それを await してから今回の flush + attach を実行する
    /// (連続切替で旧 task が新 grid load と並列実行 → stale attach 上書きする race を防止)。
    /// View bindings は <see cref="SelectedPlacement"/> を通常通り叩くだけで本 hook が呼ばれる。
    /// テスト経路は <see cref="SelectPlacementAsync"/> を直接 await することで完了を待てる
    /// (Phase B-3: 旧 fire-and-forget の Task をテストや外部呼び出しから観測可能にした)。
    /// </summary>
    partial void OnSelectedPlacementChanged(PlacementItemViewModel? value)
    {
        _ = SelectPlacementAsync(value);
    }

    private async Task FlushThenAttachAsync(
        Task previous, PlacementItemViewModel? value, GridCanvasItemViewModel? grid)
    {
        // 先行 attach の完了を待つ (失敗は観測済み扱いで握る — 今回の attach は継続させる)。
        try { await previous; } catch { }

        try { await Inspector.FlushAutoSaveAsync(CancellationToken.None); }
        catch { /* StatusMessage に反映済み想定。 attach は継続 */ }
        await Inspector.AttachAsync(value, grid);
    }

    /// <summary>
    /// 直近の SelectedPlacement 切替に伴う flush + attach の完了を待つ。
    /// 外部の遷移処理 (グリッド切替・終了等) が attach 完了後の状態を必要とするときに使う。
    /// 新規呼び出しコードは <see cref="SelectPlacementAsync"/> の戻り値を直接 await するほうが明確だが、
    /// View binding 経由 (partial void) で起動された切替はこちらでしか待てないので公開を維持する。
    /// </summary>
    public Task WaitPendingInspectorAttachAsync() => _pendingInspectorTask;

    /// <summary>CurrentGrid 変更時にも CurrentSelection を再評価する。</summary>
    partial void OnCurrentGridChanged(GridCanvasItemViewModel? value)
    {
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(CurrentSelection));
        OnPropertyChanged(nameof(IsPlacementSelected));
        OnPropertyChanged(nameof(IsVariantSelected));
        OnPropertyChanged(nameof(IsGridOnlySelected));
        OnPropertyChanged(nameof(IsNoSelection));
    }

    /// <summary>
    /// 候補ライブラリ変更通知を受信して候補を再ロードする。
    /// 失敗は握りつぶす（VM ライフサイクル中に常時購読しているため、
    /// 表示先タブが見えていない場合もある）。
    /// </summary>
    public void Receive(CopyLibraryChangedMessage message)
    {
        _ = ReloadFromMessageAsync();
    }

    /// <summary>テスト専用: Receive を await できる形で実行する。</summary>
    internal Task ReloadFromMessageAsyncForTests() => ReloadFromMessageAsync();

    private async Task ReloadFromMessageAsync()
    {
        try
        {
            await LoadCandidatesAsync();

            // 候補ライブラリ変更は cascade（Asset/Copy 削除 → 配置も削除）を伴うため、
            // DB 上の最新状態に追随するよう配置済み一覧も再ロードする。
            // LoadPlacementsAsync は差分更新化されており、PlacementId 索引で既存インスタンスを
            // 再利用するので、SelectedPlacement の参照同一性は自動的に維持される（Clear して
            // 選択を保存・復元する必要なし）。cascade 削除で消えた placement だけが Remove される。
            var grid = CurrentGrid;
            if (grid is null) return;

            await LoadPlacementsAsync(grid.GridId, default);

            // SelectedPlacement が cascade 削除で消えていたら null にフォールバック。
            if (SelectedPlacement is not null && !Placements.Contains(SelectedPlacement))
                SelectedPlacement = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogReloadFailed(_logger, ex);
        }
    }

    /// <summary>
    /// 指定したグリッドをワークスペースに読み込む。null は何も表示しないクリア状態。
    /// </summary>
    public async Task LoadGridAsync(GridCanvasItemViewModel? grid, CancellationToken ct = default)
    {
        CurrentGrid = grid;
        OnPropertyChanged(nameof(HasGrid));

        // グリッド切替は別グリッドの配置で全置換されるので、Clear で確実にリセットする
        // （LoadPlacementsAsync の差分更新も同じ結果になるが、見た目に「以前のグリッドの
        // 配置が一瞬残る」ような遷移を避けるため明示的にクリア）。
        Placements.Clear();
        SelectedPlacement = null;
        StatusMessage = null;

        // Placements を Clear したのでライブプレビュー用の lastCopyId tracking も巻き戻す。
        // attach 切替検知は次の AttachVariantPropertiesAsync / Inspector.AttachAsync から
        // 自然に再開する (lastCopyId=null → 新 CopyId への遷移として扱われ rollback は no-op)。
        _inspectorCopyPropertiesLastCopyId = null;
        _variantPropertiesLastCopyId = null;

        // SelectedPlacement = null は OnSelectedPlacementChanged 経由で FlushThenAttachAsync を
        // fire-and-forget 起動する。 そのタスクは Inspector の auto-save flush と同 DbContext での
        // EF クエリを伴うため、 後続の LoadCandidatesAsync / LoadPlacementsAsync と並走させると
        // concurrent EF クエリ race か、 旧 placement の保留中保存をロストしうる (Codex P1 指摘)。
        // 再ロード前に必ず attach タスク (内部で flush 完了を保証) の完了を待つ。
        try { await WaitPendingInspectorAttachAsync(); }
        catch { /* StatusMessage に反映済み想定。 続行する */ }

        if (grid is null)
            return;

        try
        {
            IsBusy = true;
            await LoadCandidatesAsync(ct);
            await LoadPlacementsAsync(grid.GridId, ct);
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 論理コピー候補リストを最新化する（タブ表示更新時にも呼ぶ）。
    /// 既存の <see cref="CopyCandidateViewModel"/> / <see cref="CandidateGroupViewModel"/>
    /// インスタンスは可能な限り再利用し、TreeView の展開状態 (<see cref="CandidateGroupViewModel.IsExpanded"/>)
    /// と <see cref="SelectedCandidate"/> の参照を Save / CopyLibraryChangedMessage 経由の再ロード後も維持する。
    /// </summary>
    public async Task LoadCandidatesAsync(CancellationToken ct = default)
    {
        var copies = await _copyRepository.FindAllAsync(ct);
        var assetIds = copies.Select(c => c.AssetId).Distinct().ToList();
        var assets = new Dictionary<Guid, ImageAsset>();
        foreach (var id in assetIds)
        {
            var a = await _assetRepository.FindByIdAsync(id, ct);
            if (a is not null) assets[id] = a;
        }

        // 既存 CopyCandidateViewModel を CopyId 索引で再利用、無ければ新規生成。
        // 同 CopyId のインスタンスを使い回すことで TreeView.SelectedItem の参照同一性を保つ。
        var existingByCopyId = Candidates.ToDictionary(c => c.CopyId);
        var desiredCandidates = new List<CopyCandidateViewModel>(copies.Count);
        foreach (var copy in copies)
        {
            if (!assets.TryGetValue(copy.AssetId, out var asset))
                continue;
            if (existingByCopyId.TryGetValue(copy.Id, out var existing))
            {
                // CopyName 変更を反映（OccupySize 等の他プロパティは init only なので、変更時は
                // 別 Save 経路で View が直接更新される。SummaryLine のリアルタイム更新が必要なら
                // CopyCandidateViewModel 側に [ObservableProperty] 化が必要になるが、現状は不要）。
                existing.CopyName = copy.CopyName;
                desiredCandidates.Add(existing);
            }
            else
            {
                var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
                desiredCandidates.Add(new CopyCandidateViewModel(copy, asset, thumb));
            }
        }

        SyncObservableCollection(Candidates, desiredCandidates);
        SyncCandidateGroups();

        // 選択を維持: SelectedCandidate が新リストに残っていれば（参照そのままで）OK、
        // 消えていれば null に落として最初の候補にフォールバック。
        if (SelectedCandidate is not null && !Candidates.Contains(SelectedCandidate))
            SelectedCandidate = null;
        SelectedCandidate ??= Candidates.FirstOrDefault();

        LogCandidatesLoaded(_logger, Candidates.Count);
    }

    /// <summary>
    /// <see cref="CandidateGroups"/> を <see cref="Candidates"/> に合わせて差分更新する。
    /// AssetId が一致するグループは既存インスタンスを再利用し <see cref="CandidateGroupViewModel.IsExpanded"/>
    /// を保持する。各グループの Variants も差分更新で参照同一性を保つ。
    /// </summary>
    private void SyncCandidateGroups()
    {
        var existingGroups = CandidateGroups.ToDictionary(g => g.AssetId);
        var desiredGroups = new List<CandidateGroupViewModel>();
        var variantsByAssetId = new Dictionary<Guid, List<CopyCandidateViewModel>>();

        foreach (var candidate in Candidates)
        {
            if (!variantsByAssetId.TryGetValue(candidate.AssetId, out var list))
            {
                list = [];
                variantsByAssetId[candidate.AssetId] = list;
            }
            list.Add(candidate);
        }

        foreach (var (assetId, variants) in variantsByAssetId)
        {
            CandidateGroupViewModel group;
            if (existingGroups.TryGetValue(assetId, out var existing))
            {
                group = existing;
            }
            else
            {
                group = new CandidateGroupViewModel(assetId, variants[0].AssetFilename);
            }
            SyncObservableCollection(group.Variants, variants);
            desiredGroups.Add(group);
        }

        SyncObservableCollection(CandidateGroups, desiredGroups);
    }

    /// <summary>
    /// <see cref="ObservableCollection{T}"/> を <paramref name="desired"/> の順序・要素に揃える。
    /// 既存インスタンスは参照同一性を保ったまま位置調整され、新規要素は追加、消えた要素は削除される。
    /// TreeView / ListBox の選択 / 展開状態を Clear+再構築なしに維持するためのヘルパ。
    /// </summary>
    private static void SyncObservableCollection<T>(ObservableCollection<T> target, IList<T> desired)
        where T : class
    {
        // 削除: target にあって desired にないもの
        var desiredSet = new HashSet<T>(desired);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(target[i]))
                target.RemoveAt(i);
        }

        // 並び替え + 追加: desired の順に target を整える
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i >= target.Count)
            {
                target.Add(item);
            }
            else if (!ReferenceEquals(target[i], item))
            {
                var existingIndex = target.IndexOf(item);
                if (existingIndex >= 0)
                    target.Move(existingIndex, i);
                else
                    target.Insert(i, item);
            }
        }
    }

    /// <summary>
    /// 配置リストを差分更新する（Candidates / CandidateGroups と同パターン）。
    /// PlacementId 索引で既存 <see cref="PlacementItemViewModel"/> を再利用することで、
    /// (1) GridCanvasView の Border 再生成（disk read + Skia decode/encode）を回避、
    /// (2) PlacementInspector の Inspector.AttachAsync で Attach 中だったインスタンスの
    /// 参照同一性を維持、(3) GridCanvasView の <c>OnPlacementItemPropertyChanged</c>
    /// 経由で位置・占有・PixelOffset の最小更新パスを生かす。
    /// </summary>
    private async Task LoadPlacementsAsync(Guid gridId, CancellationToken ct)
    {
        var placements = await _placementRepository.FindByGridIdAsync(gridId, ct);

        var copyCache = new Dictionary<Guid, ImageCopy>();
        var assetCache = new Dictionary<Guid, ImageAsset>();
        var existingByPlacementId = Placements.ToDictionary(p => p.PlacementId);
        var desired = new List<PlacementItemViewModel>(placements.Count);

        foreach (var p in placements)
        {
            if (!copyCache.TryGetValue(p.CopyId, out var copy))
            {
                copy = await _copyRepository.FindByIdAsync(p.CopyId, ct);
                if (copy is null) continue;
                copyCache[p.CopyId] = copy;
            }

            if (!assetCache.TryGetValue(copy.AssetId, out var asset))
            {
                asset = await _assetRepository.FindByIdAsync(copy.AssetId, ct);
                if (asset is null) continue;
                assetCache[copy.AssetId] = asset;
            }

            PlacementItemViewModel item;
            if (existingByPlacementId.TryGetValue(p.Id, out var existing))
            {
                // 既存インスタンスを最新の DB 値で同期（参照同一性を保つ）。
                // ApplyCopyChanges で共有特性を、各 setter で配置固有特性を反映。
                existing.Position = p.Position;
                existing.OccupySize = p.OccupySize;
                existing.PixelOffsetX = p.PixelOffsetX;
                existing.PixelOffsetY = p.PixelOffsetY;
                existing.ApplyCopyChanges(copy);
                item = existing;
            }
            else
            {
                var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
                item = new PlacementItemViewModel(p, copy, asset, thumb);
            }

            // ManualCrop / AutoCrop の優先順位を Resolver で解決し、実効的なクロップ比率を
            // PlacementItemViewModel.EffectiveCropFraction に保存。Renderer / View / Use case が
            // 同一比率を共有することで、自動と手動の表示が揃う。
            try
            {
                item.EffectiveCropFraction = await _cropResolver.ResolveAsync(copy, asset, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 走査失敗時はクロップなしで表示（fallback）
                item.EffectiveCropFraction = null;
            }

            desired.Add(item);
        }

        SyncObservableCollection(Placements, desired);

        LogPlacementsLoaded(_logger, gridId, Placements.Count);
    }

    /// <summary>
    /// 配置済みアイテムをドロップ位置へ移動、または既存配置と入れ替える。
    /// 実処理は <see cref="MovePlacementInternalAsync"/> / <see cref="SwapPlacementsInternalAsync"/> に委譲し、
    /// ここでは guard / source-target 解決 / IsBusy 制御のみを担う。
    /// </summary>
    public async Task<bool> MoveOrSwapPlacementAsync(
        Guid sourcePlacementId,
        CellPosition dropPosition,
        CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return false;

        var source = Placements.FirstOrDefault(p => p.PlacementId == sourcePlacementId);
        if (source is null) return false;

        var target = FindOverlappingPlacement(sourcePlacementId, dropPosition);

        try
        {
            IsBusy = true;
            return target is null
                ? await MovePlacementInternalAsync(source, grid, dropPosition, ct)
                : await SwapPlacementsInternalAsync(source, target, grid, ct);
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 指定セルと矩形が重なる別の placement を探す (sourcePlacementId は除外)。
    /// 見つからなければ <c>null</c>。 OccupySize は最低 1 で扱う。
    /// </summary>
    private PlacementItemViewModel? FindOverlappingPlacement(Guid sourcePlacementId, CellPosition cell) =>
        Placements.FirstOrDefault(p =>
            p.PlacementId != sourcePlacementId &&
            cell.X >= p.GridX && cell.X < p.GridX + Math.Max(1, p.OccupyWidth) &&
            cell.Y >= p.GridY && cell.Y < p.GridY + Math.Max(1, p.OccupyHeight));

    /// <summary>
    /// Move 経路。 Position だけが変わるため <see cref="ReloadPlacementsAsync"/> (全件再ロード +
    /// AutoCrop 再走査) は呼ばない。 <c>PlacementItemViewModel.Position</c> は ObservableProperty なので
    /// View binding が即座に追従する。 同位置への drop は no-op で <c>false</c> を返す。
    /// </summary>
    private async Task<bool> MovePlacementInternalAsync(
        PlacementItemViewModel source, GridCanvasItemViewModel grid,
        CellPosition dropPosition, CancellationToken ct)
    {
        var beforePosition = source.Position;
        if (beforePosition == dropPosition) return false;

        var moveDescription =
            $"移動: 「{source.Label}」 ({beforePosition.X},{beforePosition.Y}) → ({dropPosition.X},{dropPosition.Y})";
        var command = new MovePlacementCommand(
            _moveUseCase, grid.GridId, source.PlacementId, beforePosition, dropPosition, moveDescription);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }
        source.Position = dropPosition;
        SelectedPlacement = source;
        StatusMessage = _loc.Format("Status_PlacementMovedFmt", dropPosition.X, dropPosition.Y);
        return true;
    }

    /// <summary>
    /// Swap 経路。 Position の交換のみで View は反応するため全件再ロード不要。
    /// </summary>
    private async Task<bool> SwapPlacementsInternalAsync(
        PlacementItemViewModel source, PlacementItemViewModel target,
        GridCanvasItemViewModel grid, CancellationToken ct)
    {
        var swapDescription = $"入れ替え: 「{source.Label}」⇔「{target.Label}」";
        var command = new SwapPlacementsCommand(
            _swapUseCase, grid.GridId, source.PlacementId, target.PlacementId, swapDescription);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }
        var sourceOldPosition = source.Position;
        source.Position = target.Position;
        target.Position = sourceOldPosition;
        SelectedPlacement = source;
        StatusMessage = _loc["Status_PlacementSwapped"];
        return true;
    }

    /// <summary>
    /// 指定した論理コピーを指定セルに配置する。D&amp;D のドロップハンドラから呼ばれる。
    /// </summary>
    public async Task<bool> PlaceCopyAtAsync(Guid copyId, CellPosition position, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return false;

        try
        {
            IsBusy = true;
            var candidate = Candidates.FirstOrDefault(c => c.CopyId == copyId);
            var copyLabel = candidate?.CopyDisplayName ?? _loc[Terminology.VariantUnknownKey];
            var description = _loc.Format("History_PlacementPlacedFmt", copyLabel, position.X, position.Y);
            var command = new PlaceCommand(
                _placeUseCase, _removeUseCase, _placementRepository,
                grid.GridId, copyId, position, description);
            var result = await _history.ExecuteAsync(command, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return false;
            }

            // 局所追加: 新規 1 件だけ取得して Placements に Add する。
            // 全件再ロードは不要（既存配置は何も変わらない）。
            var added = command.CreatedPlacementId is { } pid
                ? await AppendPlacementToViewAsync(pid, ct)
                : null;
            SelectedPlacement = added;
            StatusMessage = _loc.Format("Status_PlacementPlacedFmt", position.X, position.Y);
            return true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task PlaceSelectedToFirstFreeCellAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        var candidate = SelectedCandidate;
        if (grid is null || candidate is null || IsBusy) return;

        try
        {
            IsBusy = true;
            var position = FindFirstFreeCell(grid, candidate.OccupySize);
            if (position is null)
            {
                StatusMessage = _loc["Status_NoFreeCells"];
                return;
            }

            var description = _loc.Format("History_PlacementPlacedFmt", candidate.CopyDisplayName, position.Value.X, position.Value.Y);
            var command = new PlaceCommand(
                _placeUseCase, _removeUseCase, _placementRepository,
                grid.GridId, candidate.CopyId, position.Value, description);
            var result = await _history.ExecuteAsync(command, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            // 局所追加: 新規 1 件のみ取得して Placements に Add する。
            var added = command.CreatedPlacementId is { } pid
                ? await AppendPlacementToViewAsync(pid, ct)
                : null;
            SelectedPlacement = added;
            StatusMessage = _loc.Format("Status_PlacementPlacedFmt", position.Value.X, position.Value.Y);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RemoveSelectedPlacementAsync(CancellationToken ct = default)
    {
        var target = SelectedPlacement;
        if (target is null || IsBusy) return;

        try
        {
            IsBusy = true;
            // Undo に必要な完全 snapshot を Execute 前に取得
            var snapshot = await _placementRepository.FindByIdAsync(target.PlacementId, ct);
            if (snapshot is null)
            {
                StatusMessage = _loc["Status_PlacementNotFoundForRemove"];
                return;
            }

            var description = _loc.Format("History_PlacementRemovedFmt", target.Label, target.GridX, target.GridY);
            var command = new RemovePlacementCommand(
                _removeUseCase, _placementRepository, snapshot, description);
            var result = await _history.ExecuteAsync(command, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            Placements.Remove(target);
            SelectedPlacement = Placements.FirstOrDefault();
            StatusMessage = _loc["Status_PlacementRemoved"];
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadPlacementsAsync(Guid gridId, CancellationToken ct)
    {
        // 差分更新により Clear は不要（LoadPlacementsAsync が消えた placement を Remove する）。
        // SelectedPlacement の参照同一性も維持される。
        await LoadPlacementsAsync(gridId, ct);
    }

    /// <summary>
    /// 新規配置 1 件だけを取得して <see cref="Placements"/> に Add する局所最適化版。
    /// 既存配置への影響がないケース（PlaceCopyAtAsync / PlaceSelectedToFirstFreeCellAsync）で
    /// 全件再ロード（DB 全件 SELECT + 既存全インスタンスへの代入）を回避する。
    /// </summary>
    private async Task<PlacementItemViewModel?> AppendPlacementToViewAsync(Guid placementId, CancellationToken ct)
    {
        var p = await _placementRepository.FindByIdAsync(placementId, ct);
        if (p is null) return null;
        var copy = await _copyRepository.FindByIdAsync(p.CopyId, ct);
        if (copy is null) return null;
        var asset = await _assetRepository.FindByIdAsync(copy.AssetId, ct);
        if (asset is null) return null;

        var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
        var item = new PlacementItemViewModel(p, copy, asset, thumb);

        try
        {
            item.EffectiveCropFraction = await _cropResolver.ResolveAsync(copy, asset, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            item.EffectiveCropFraction = null;
        }

        Placements.Add(item);
        return item;
    }

    /// <summary>
    /// 左上から走査して、占有サイズが収まる最初の空きセルを返す。
    /// </summary>
    private CellPosition? FindFirstFreeCell(GridCanvasItemViewModel grid, OccupySize size)
    {
        var existing = Placements
            .Select(p => new ExistingPlacement(p.PlacementId, p.Position, p.OccupySize))
            .ToArray();

        for (var y = 0; y <= grid.Rows - size.Height; y++)
        {
            for (var x = 0; x <= grid.Cols - size.Width; x++)
            {
                var pos = new CellPosition(x, y);
                var validation = PlacementValidator.Validate(
                    size, pos, grid.Rows, grid.Cols, existing);
                if (validation.IsValid)
                    return pos;
            }
        }
        return null;
    }

    // RequestPreviewAsync / ExportToPngAsync / SavePngBytesAsync / SanitizeFileName は
    // Phase 4 で GridOutputViewModel に移管。 Output プロパティ経由でアクセスする (`Output.RequestPreviewAsync` 等)。

    // ApplyGridWeightsAsync / BuildAfterWeights / ResolveWeightsChangeFormatKey は
    // GridStructureEditorViewModel へ移管 (Phase 4-3)。 既存呼出元 (Code-behind GridCanvasView /
    // テスト) との後方互換のため、 本 VM 上に薄い wrapper を残置して Structure に転送する。
    public Task<bool> ApplyGridWeightsAsync(
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CancellationToken ct = default) =>
        Structure.ApplyGridWeightsAsync(colWeights, rowWeights, ct);

    /// <summary>
    /// Shift+ドラッグ等のキャンバス操作で配置の <see cref="GridPlacement.PixelOffsetX"/> /
    /// <c>PixelOffsetY</c> を永続化する。値は <see cref="PlacementInspectorViewModel.MaxPixelOffset"/>
    /// で clamp される。成功時は VM 内の対応 <see cref="PlacementItemViewModel"/> も同期更新するため、
    /// 再ロードは不要。
    /// </summary>
    public async Task<bool> ApplyPixelOffsetAsync(
        Guid placementId, int pixelOffsetX, int pixelOffsetY, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;

        var max = PlacementInspectorViewModel.MaxPixelOffset;
        var clampedX = Math.Clamp(pixelOffsetX, -max, max);
        var clampedY = Math.Clamp(pixelOffsetY, -max, max);

        // Undo に備えて DB 上の現在値を before として取得（VM 側はドラッグ中に書き換わっているため）
        var current = await _placementRepository.FindByIdAsync(placementId, ct);
        if (current is null)
        {
            StatusMessage = _loc.Format("Status_PlacementNotFoundFmt", placementId);
            return false;
        }
        var beforeX = current.PixelOffsetX;
        var beforeY = current.PixelOffsetY;

        if (beforeX == clampedX && beforeY == clampedY)
        {
            // 値変化なし — 履歴に積まない
            StatusMessage = _loc["Status_PixelOffsetNoChange"];
            return true;
        }

        var item = Placements.FirstOrDefault(p => p.PlacementId == placementId);
        var label = item?.Label ?? _loc["History_UnknownPlacement"];
        var description = _loc.Format("History_PixelOffsetFmt", label, clampedX, clampedY);
        var command = new UpdatePlacementOffsetCommand(
            _updateOffsetUseCase, grid.GridId, placementId,
            beforeX, beforeY, clampedX, clampedY, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        if (item is not null)
        {
            item.PixelOffsetX = clampedX;
            item.PixelOffsetY = clampedY;
        }
        StatusMessage = _loc.Format("Status_PixelOffsetSavedFmt", clampedX, clampedY);
        return true;
    }

    // FitGridWeightAsync / ToggleColLockAsync / ToggleRowLockAsync / ToggleAxisLockAsync /
    // NormalizeLocks は GridStructureEditorViewModel へ移管 (Phase 4-3)。
    // 既存呼出元 (Code-behind GridCanvasView / テスト) との後方互換のため、 本 VM 上に薄い wrapper を残置。
    public Task<bool> FitGridWeightAsync(Guid placementId, FitAxis axis, CancellationToken ct = default) =>
        Structure.FitGridWeightAsync(placementId, axis, ct);

    public Task<bool> ToggleColLockAsync(int colIndex, CancellationToken ct = default) =>
        Structure.ToggleColLockAsync(colIndex, ct);

    public Task<bool> ToggleRowLockAsync(int rowIndex, CancellationToken ct = default) =>
        Structure.ToggleRowLockAsync(rowIndex, ct);

    // バリアント新規作成 / 削除 / インラインリネームは VariantManagerViewModel へ移管 (Phase 4-2)。
    // View からは {Binding Variants.BeginCreateVariantCommand} 等。
    // BeginEditCandidate / CancelEditCandidate / CommitEditCandidateAsync は code-behind から
    // vm.Variants.BeginEditCandidate(...) のように呼ぶ。

    /// <summary>
    /// SelectedCandidate 変化時の hook。 Variants 子 VM のコマンド CanExecute 再評価と、
    /// CurrentSelection の再評価 (バリアント選択時に VariantSelection 状態へ遷移)、
    /// および VariantProperties の編集対象差し替えを行う。
    /// </summary>
    partial void OnSelectedCandidateChanged(CopyCandidateViewModel? value)
    {
        Variants.NotifyContextChanged();

        // ユーザーが候補リストで配置中とは別のバリアントを選んだら、配置選択を解除して
        // 右ペインをバリアント単体編集 (VariantSelection) に切り替える。配置選択が優先される
        // 仕様上、これをしないと「セル選択中はバリアントを変えても右ペインが無反応」になる。
        // 配置由来の同期 (_syncingCandidateFromPlacement) はユーザー操作ではないので解除しない。
        if (!_syncingCandidateFromPlacement
            && value is not null
            && SelectedPlacement is { } placement
            && placement.CopyId != value.CopyId)
        {
            SelectedPlacement = null;
        }

        NotifySelectionChanged();
        _ = AttachVariantPropertiesAsync(value);
    }

    /// <summary>
    /// VariantProperties (右ペイン スタンドアロン CopyPropertiesView の VM) に
    /// SelectedCandidate の variant データを attach する。 失敗時は null attach で無効状態に。
    /// </summary>
    private async Task AttachVariantPropertiesAsync(CopyCandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            VariantProperties.Attach(null);
            return;
        }

        try
        {
            var copy = await _copyRepository.FindByIdAsync(candidate.CopyId);
            var asset = copy is null ? null : await _assetRepository.FindByIdAsync(copy.AssetId);
            if (copy is null || asset is null)
            {
                VariantProperties.Attach(null);
                return;
            }
            var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var sourcePath = _imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            var item = new CopyItemViewModel(copy, thumb, sourcePath, asset.Size.Width, asset.Size.Height);
            VariantProperties.Attach(item);
            _variantAutoSave.ResetFailureGuard();
        }
        catch
        {
            VariantProperties.Attach(null);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        Variants.NotifyContextChanged();
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "配置候補を読み込み: {Count} 件")]
    private static partial void LogCandidatesLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "配置を読み込み: grid={GridId} count={Count}")]
    private static partial void LogPlacementsLoaded(ILogger logger, Guid gridId, int count);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Warning, Message = "候補・配置の自動更新に失敗")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex);

    // LogVariantCreated (5004) / LogVariantDeleted (5005) / LogVariantRenamed (5006) /
    // LogVariantRenameCanceled (5007) は VariantManagerViewModel へ移管 (Phase 4-2)。

    // LogPreviewRendered (EventId 5008) / LogPngExported (EventId 5009) は GridOutputViewModel へ移管 (Phase 4)。

    /// <summary>
    /// <see cref="IGridStructureEditorContext.NotifyCurrentGridChanged"/> の実装。
    /// 子 VM (Structure) が CurrentGrid 内部 (重み / ロック) を更新した後に呼び、
    /// View binding (GridCanvasView の Rebuild 等) を再評価させる。
    /// </summary>
    public void NotifyCurrentGridChanged() => OnPropertyChanged(nameof(CurrentGrid));

    public void Dispose()
    {
        // 構成で張った全 subscription を解除して循環参照を断つ (Codex review 指摘の dispose 漏れ修正)。
        // Inspector.CopyProperties / VariantProperties は別々の CopyPropertiesViewModel インスタンスで、
        // Inspector.Dispose() が前者を破棄するため、 こちらは subscription 解除のみ担当。
        VariantProperties.PropertyChanged -= OnVariantPropertiesChanged;
        VariantProperties.DraftReverted -= OnVariantPropertiesDraftReverted;
        Inspector.CopyProperties.PropertyChanged -= OnInspectorCopyPropertiesChanged;
        Inspector.CopyProperties.DraftReverted -= OnInspectorCopyPropertiesDraftReverted;
        _appSettings.Changed -= OnAppSettingsChangedForVariant;
        _variantAutoSave.Dispose();
        _messenger.UnregisterAll(this);
        Inspector.Dispose();
        // VariantProperties は Inspector.CopyProperties とは別インスタンスで、 GridWorkspaceVM が
        // 構築・所有しているので明示的に Dispose する (AutoCropPreview CTS を解放するため)。
        VariantProperties.Dispose();
    }
}
