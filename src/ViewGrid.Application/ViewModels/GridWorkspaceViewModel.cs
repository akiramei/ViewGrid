using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
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
public sealed partial class GridWorkspaceViewModel : ViewModelBase, IRecipient<CopyLibraryChangedMessage>
{
    private readonly IGridCanvasRepository _gridRepository;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IImageStorage _imageStorage;
    private readonly IImageCropResolver _cropResolver;
    private readonly PlaceImageCopyUseCase _placeUseCase;
    private readonly RemovePlacementUseCase _removeUseCase;
    private readonly MovePlacementUseCase _moveUseCase;
    private readonly SwapPlacementsUseCase _swapUseCase;
    private readonly RenderGridUseCase _renderUseCase;
    private readonly ExportGridUseCase _exportUseCase;
    private readonly UpdateGridWeightsUseCase _updateWeightsUseCase;
    private readonly UpdateGridLocksUseCase _updateLocksUseCase;
    private readonly UpdatePlacementOffsetUseCase _updateOffsetUseCase;
    private readonly FitGridWeightToPlacementUseCase _fitWeightUseCase;
    private readonly CreateLogicalCopyUseCase _createCopyUseCase;
    private readonly UpdateImageCopyUseCase _updateCopyUseCase;
    private readonly IFilePickerService _filePicker;
    private readonly IMessenger _messenger;
    private readonly IUndoRedoService _history;
    private readonly ILogger<GridWorkspaceViewModel> _logger;

    public PlacementInspectorViewModel Inspector { get; }

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
    /// プレビュー / PNG 出力で適用するトリミング設定。配置タブの右ペイン上部の
    /// ComboBox から選択する。<see cref="TrimMode.None"/> はキャンバス全面、
    /// <see cref="TrimMode.OccupiedCells"/> は占有セルの bbox で切り出し、
    /// <see cref="TrimMode.DrawnPixels"/> は α&gt;0 のピクセル走査で求めた bbox で切り出し。
    /// 永続化はせず、セッション内のオプション扱い（既定 None）。
    /// </summary>
    [ObservableProperty]
    public partial TrimMode SelectedTrimMode { get; set; } = TrimMode.None;

    public IReadOnlyList<TrimMode> TrimModeOptions { get; } =
        [TrimMode.None, TrimMode.OccupiedCells, TrimMode.DrawnPixels];

    /// <summary>
    /// 「+ 新規バリアント」フライアウトを開いているか。<c>true</c> の間だけ View 側で名前入力 TextBox と
    /// 確定/キャンセルボタンが表示される（<see cref="CopyListViewModel.IsCreating"/> と同パターン）。
    /// 生成先のアセットは <see cref="SelectedCandidate"/> の <see cref="CopyCandidateViewModel.AssetId"/>。
    /// </summary>
    [ObservableProperty]
    public partial bool IsCreatingVariant { get; set; }

    /// <summary>
    /// 新規作成フライアウトの名前ドラフト。空白だけ / 空文字なら「バリアント N」自動採番、
    /// 値があればそれを <see cref="CreateLogicalCopyUseCase"/> に渡す。
    /// </summary>
    [ObservableProperty]
    public partial string DraftVariantName { get; set; } = string.Empty;

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
            if (CurrentGrid is null) return NoSelection.Instance;
            if (SelectedPlacement is { } p)
                return new PlacementSelection(CurrentGrid.GridId, p.PlacementId, p.CopyId);
            return new GridSelection(CurrentGrid.GridId);
        }
    }

    /// <summary>View 側で <c>IsVisible</c> バインドを使う場合の利便プロパティ（DataTemplate 切替なら不要）。</summary>
    public bool IsPlacementSelected => CurrentSelection is PlacementSelection;
    public bool IsGridOnlySelected => CurrentSelection is GridSelection;
    public bool IsNoSelection => CurrentSelection is NoSelection;

    public GridWorkspaceViewModel(
        IGridCanvasRepository gridRepository,
        IImageCopyRepository copyRepository,
        IImageAssetRepository assetRepository,
        IGridPlacementRepository placementRepository,
        IThumbnailService thumbnailService,
        IImageStorage imageStorage,
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
        IFilePickerService filePicker,
        IMessenger messenger,
        IUndoRedoService history,
        PlacementInspectorViewModel inspector,
        ILogger<GridWorkspaceViewModel> logger)
    {
        _gridRepository = gridRepository;
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _placementRepository = placementRepository;
        _thumbnailService = thumbnailService;
        _imageStorage = imageStorage;
        _cropResolver = cropResolver;
        _placeUseCase = placeUseCase;
        _removeUseCase = removeUseCase;
        _moveUseCase = moveUseCase;
        _swapUseCase = swapUseCase;
        _renderUseCase = renderUseCase;
        _exportUseCase = exportUseCase;
        _updateWeightsUseCase = updateWeightsUseCase;
        _updateLocksUseCase = updateLocksUseCase;
        _updateOffsetUseCase = updateOffsetUseCase;
        _fitWeightUseCase = fitWeightUseCase;
        _createCopyUseCase = createCopyUseCase;
        _updateCopyUseCase = updateCopyUseCase;
        _filePicker = filePicker;
        _messenger = messenger;
        _history = history;
        Inspector = inspector;
        _logger = logger;

        _messenger.Register(this);
    }

    /// <summary>SelectedPlacement 変更時に Inspector を Attach し、CurrentSelection も再計算する。
    /// 描画域サイズの計算に CurrentGrid（重み・キャンバスサイズ）が必要なので渡す。</summary>
    partial void OnSelectedPlacementChanged(PlacementItemViewModel? value)
    {
        _ = Inspector.AttachAsync(value, CurrentGrid);
        NotifySelectionChanged();
    }

    /// <summary>CurrentGrid 変更時にも CurrentSelection を再評価する。</summary>
    partial void OnCurrentGridChanged(GridCanvasItemViewModel? value)
    {
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(CurrentSelection));
        OnPropertyChanged(nameof(IsPlacementSelected));
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
            var grid = CurrentGrid;
            if (grid is null) return;

            var previousSelectedId = SelectedPlacement?.PlacementId;
            Placements.Clear();
            SelectedPlacement = null;
            await LoadPlacementsAsync(grid.GridId, default);

            // 再ロード前に選択していた配置がまだ存在するなら新インスタンスを選び直す
            // （Inspector の表示が消えないようにするため）
            if (previousSelectedId is not null)
                SelectedPlacement = Placements.FirstOrDefault(p => p.PlacementId == previousSelectedId.Value);
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

        Placements.Clear();
        SelectedPlacement = null;
        StatusMessage = null;

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

    private async Task LoadPlacementsAsync(Guid gridId, CancellationToken ct)
    {
        var placements = await _placementRepository.FindByGridIdAsync(gridId, ct);

        var copyCache = new Dictionary<Guid, ImageCopy>();
        var assetCache = new Dictionary<Guid, ImageAsset>();

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

            var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var item = new PlacementItemViewModel(p, copy, asset, thumb);

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

            Placements.Add(item);
        }

        LogPlacementsLoaded(_logger, gridId, Placements.Count);
    }

    /// <summary>
    /// 配置済みアイテムをドロップ位置へ移動、または既存配置と入れ替える。
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

        // ドロップ先セルにある別の配置を探す
        var target = Placements.FirstOrDefault(p =>
            p.PlacementId != sourcePlacementId &&
            dropPosition.X >= p.GridX && dropPosition.X < p.GridX + Math.Max(1, p.OccupyWidth) &&
            dropPosition.Y >= p.GridY && dropPosition.Y < p.GridY + Math.Max(1, p.OccupyHeight));

        try
        {
            IsBusy = true;
            if (target is null)
            {
                // Move（Undo/Redo 履歴に積む）
                var beforePosition = source.Position;
                if (beforePosition == dropPosition) return false;
                var moveDescription =
                    $"移動: 「{source.Label}」 ({beforePosition.X},{beforePosition.Y}) → ({dropPosition.X},{dropPosition.Y})";
                var command = new MovePlacementCommand(
                    _moveUseCase, grid.GridId, sourcePlacementId, beforePosition, dropPosition, moveDescription);
                var result = await _history.ExecuteAsync(command, ct);
                if (result.IsError)
                {
                    StatusMessage = string.Join(", ", result.Errors);
                    return false;
                }
                // Move では Position だけが変わる。共有特性 / Crop 設定 / サムネは不変なので、
                // ReloadPlacementsAsync (全件再ロード + AutoCrop 再走査) は不要。
                // PlacementItemViewModel.Position は ObservableProperty なので View が反応する。
                source.Position = dropPosition;
                SelectedPlacement = source;
                StatusMessage = $"({dropPosition.X},{dropPosition.Y}) に移動しました。";
                return true;
            }
            else
            {
                // Swap（Undo/Redo 履歴に積む）
                var swapDescription = $"入れ替え: 「{source.Label}」⇔「{target.Label}」";
                var command = new SwapPlacementsCommand(
                    _swapUseCase, grid.GridId, sourcePlacementId, target.PlacementId, swapDescription);
                var result = await _history.ExecuteAsync(command, ct);
                if (result.IsError)
                {
                    StatusMessage = string.Join(", ", result.Errors);
                    return false;
                }
                // Swap も Position の交換のみで View は反応する。同上の理由で全件再ロード不要。
                var sourceOldPosition = source.Position;
                source.Position = target.Position;
                target.Position = sourceOldPosition;
                SelectedPlacement = source;
                StatusMessage = "配置を入れ替えました。";
                return true;
            }
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 指定した論理コピーを指定セルに配置する。D&D のドロップハンドラから呼ばれる。
    /// </summary>
    public async Task<bool> PlaceCopyAtAsync(Guid copyId, CellPosition position, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return false;

        try
        {
            IsBusy = true;
            var candidate = Candidates.FirstOrDefault(c => c.CopyId == copyId);
            var copyLabel = candidate?.CopyDisplayName ?? Terminology.VariantUnknown;
            var description = $"配置: 「{copyLabel}」→ ({position.X},{position.Y})";
            var command = new PlaceCommand(
                _placeUseCase, _removeUseCase, _placementRepository,
                grid.GridId, copyId, position, description);
            var result = await _history.ExecuteAsync(command, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return false;
            }

            await ReloadPlacementsAsync(grid.GridId, ct);
            SelectedPlacement = command.CreatedPlacementId is { } pid
                ? Placements.FirstOrDefault(p => p.PlacementId == pid)
                : null;
            StatusMessage = $"({position.X},{position.Y}) に配置しました。";
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
                StatusMessage = "空きセルが見つかりません。";
                return;
            }

            var description = $"配置: 「{candidate.CopyDisplayName}」→ ({position.Value.X},{position.Value.Y})";
            var command = new PlaceCommand(
                _placeUseCase, _removeUseCase, _placementRepository,
                grid.GridId, candidate.CopyId, position.Value, description);
            var result = await _history.ExecuteAsync(command, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            // 反映: VM 側のリストにアイテムを追加（再ロードで一括更新する）
            await ReloadPlacementsAsync(grid.GridId, ct);
            SelectedPlacement = command.CreatedPlacementId is { } pid
                ? Placements.FirstOrDefault(p => p.PlacementId == pid)
                : null;
            StatusMessage = $"({position.Value.X},{position.Value.Y}) に配置しました。";
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
                StatusMessage = "削除対象の配置が見つかりません。";
                return;
            }

            var description = $"削除: 「{target.Label}」 ({target.GridX},{target.GridY})";
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
            StatusMessage = "配置を削除しました。";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadPlacementsAsync(Guid gridId, CancellationToken ct)
    {
        Placements.Clear();
        await LoadPlacementsAsync(gridId, ct);
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

    /// <summary>
    /// 現在のグリッドをレンダリングして PNG バイト列を返す（プレビュー用）。
    /// 失敗時は <c>null</c> を返し、<see cref="StatusMessage"/> にエラーを格納する。
    /// </summary>
    public async Task<byte[]?> RequestPreviewAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return null;

        try
        {
            IsBusy = true;
            var result = await _renderUseCase.ExecuteAsync(grid.GridId, SelectedTrimMode, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return null;
            }
            StatusMessage = $"プレビューを生成しました ({result.Value.Length:N0} bytes)";
            return result.Value;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// SaveDialog を出して指定パスへ PNG として書き出す。プレビュー経由しない高速パス。
    /// </summary>
    [RelayCommand]
    public async Task ExportToPngAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return;

        var suggested = $"{SanitizeFileName(grid.Name)}.png";
        var path = await _filePicker.PickSavePngPathAsync(suggested, ct);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            IsBusy = true;
            var result = await _exportUseCase.ExecuteAsync(grid.GridId, path, SelectedTrimMode, ct);
            StatusMessage = result.IsError
                ? string.Join(", ", result.Errors)
                : $"出力しました: {Path.GetFileName(path)} ({result.Value.FileSizeBytes:N0} bytes)";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// プレビューで生成した既存 PNG バイト列を、SaveDialog で選んだパスに書き出す。
    /// </summary>
    public async Task<bool> SavePngBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || bytes is null || bytes.Length == 0) return false;

        var suggested = $"{SanitizeFileName(grid.Name)}.png";
        var path = await _filePicker.PickSavePngPathAsync(suggested, ct);
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            StatusMessage = $"出力しました: {Path.GetFileName(path)} ({bytes.LongLength:N0} bytes)";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"保存に失敗しました: {ex.Message}";
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "viewgrid-export" : clean;
    }

    /// <summary>
    /// 境界ドラッグでの重み更新を保存する。<paramref name="colWeights"/> または
    /// <paramref name="rowWeights"/> のどちらかが null（変更なし）でも構わない。
    /// 成功時は <see cref="CurrentGrid"/> の重みを更新して View にリビルドさせる。
    /// </summary>
    public async Task<bool> ApplyGridWeightsAsync(
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;

        // Undo に備えて before/after の完全な配列を保持。null（変更なし）は現在値で埋める。
        var beforeCol = grid.ColWeights;
        var beforeRow = grid.RowWeights;
        var afterCol = colWeights is null
            ? beforeCol
            : [.. colWeights];
        var afterRow = rowWeights is null
            ? beforeRow
            : [.. rowWeights];

        if (afterCol.SequenceEqual(beforeCol) && afterRow.SequenceEqual(beforeRow))
            return true; // 値変化なし — 履歴に積まない

        // どちらが変わったかでラベルを切り替え（両方変わったら「比率」とまとめる）
        var colChanged = !afterCol.SequenceEqual(beforeCol);
        var rowChanged = !afterRow.SequenceEqual(beforeRow);
        var axisLabel = colChanged && rowChanged ? "比率" : (colChanged ? "列幅" : "行高");
        var description = $"{axisLabel}変更: グリッド「{grid.Name}」";
        var command = new UpdateGridWeightsCommand(
            _updateWeightsUseCase, grid.GridId, beforeCol, beforeRow, afterCol, afterRow, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // 永続化された最新値を再取得して VM に反映
        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is not null)
        {
            grid.ColWeights = reloaded.ColWeights;
            grid.RowWeights = reloaded.RowWeights;
            OnPropertyChanged(nameof(CurrentGrid));
        }
        StatusMessage = "グリッド比率を更新しました。";
        return true;
    }

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
            StatusMessage = $"GridPlacement {placementId} が見つかりません。";
            return false;
        }
        var beforeX = current.PixelOffsetX;
        var beforeY = current.PixelOffsetY;

        if (beforeX == clampedX && beforeY == clampedY)
        {
            // 値変化なし — 履歴に積まない
            StatusMessage = "ピクセル微調整: 変化なし。";
            return true;
        }

        var item = Placements.FirstOrDefault(p => p.PlacementId == placementId);
        var label = item?.Label ?? "(不明な配置)";
        var description = $"ピクセル微調整: 「{label}」 ΔX={clampedX}, ΔY={clampedY}";
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
        StatusMessage = $"ピクセル微調整を保存しました (ΔX={clampedX}, ΔY={clampedY})。";
        return true;
    }

    /// <summary>
    /// 指定 placement の実描画矩形に合わせて、占有列幅または行高を縮める。
    /// 余白は隣接列/行に分配（端列/端行で隣接がない側の余白は破棄）。
    /// 成功時は最新の重みを <see cref="CurrentGrid"/> に反映し、View を再構築させる。
    /// </summary>
    public async Task<bool> FitGridWeightAsync(
        Guid placementId, FitAxis axis, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;

        var oldCol = grid.ColWeights;
        var oldRow = grid.RowWeights;

        var result = await _fitWeightUseCase.ExecuteAsync(placementId, axis, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // 重みが変わった可能性があるので、グリッドを再読込して反映
        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is null) return false;

        var changed =
            !reloaded.ColWeights.SequenceEqual(oldCol) ||
            !reloaded.RowWeights.SequenceEqual(oldRow);

        grid.ColWeights = reloaded.ColWeights;
        grid.RowWeights = reloaded.RowWeights;
        OnPropertyChanged(nameof(CurrentGrid));

        StatusMessage = changed
            ? (axis == FitAxis.Column ? "列幅を画像にフィットしました。" : "行高を画像にフィットしました。")
            : "フィット対象なし（余白がない、または計算範囲外）。";
        return true;
    }

    /// <summary>
    /// 指定列のロック状態を反転する（true ↔ false）。
    /// 成功時は <see cref="CurrentGrid"/> の <see cref="GridCanvasItemViewModel.ColLocked"/>
    /// も更新して View を再構築させる。
    /// </summary>
    public async Task<bool> ToggleColLockAsync(int colIndex, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;
        if (colIndex < 0 || colIndex >= grid.Cols) return false;

        var beforeCol = grid.ColLocked.Length == grid.Cols
            ? grid.ColLocked
            : [.. Enumerable.Range(0, grid.Cols).Select(_ => false)];
        var afterCol = beforeCol.SetItem(colIndex, !beforeCol[colIndex]);
        var beforeRow = grid.RowLocked.Length == grid.Rows
            ? grid.RowLocked
            : [.. Enumerable.Range(0, grid.Rows).Select(_ => false)];

        var lockState = afterCol[colIndex] ? "ロック" : "解除";
        var description = $"列 {colIndex} {lockState}: グリッド「{grid.Name}」";
        var command = new UpdateGridLocksCommand(
            _updateLocksUseCase, grid.GridId, beforeCol, beforeRow, afterCol, beforeRow, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is not null)
        {
            grid.ColLocked = reloaded.ColLocked;
            OnPropertyChanged(nameof(CurrentGrid));
        }
        StatusMessage = afterCol[colIndex] ? $"列 {colIndex} をロックしました。" : $"列 {colIndex} のロックを解除しました。";
        return true;
    }

    /// <summary>指定行のロック状態を反転する。</summary>
    public async Task<bool> ToggleRowLockAsync(int rowIndex, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;
        if (rowIndex < 0 || rowIndex >= grid.Rows) return false;

        var beforeRow = grid.RowLocked.Length == grid.Rows
            ? grid.RowLocked
            : [.. Enumerable.Range(0, grid.Rows).Select(_ => false)];
        var afterRow = beforeRow.SetItem(rowIndex, !beforeRow[rowIndex]);
        var beforeCol = grid.ColLocked.Length == grid.Cols
            ? grid.ColLocked
            : [.. Enumerable.Range(0, grid.Cols).Select(_ => false)];

        var lockState = afterRow[rowIndex] ? "ロック" : "解除";
        var description = $"行 {rowIndex} {lockState}: グリッド「{grid.Name}」";
        var command = new UpdateGridLocksCommand(
            _updateLocksUseCase, grid.GridId, beforeCol, beforeRow, beforeCol, afterRow, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is not null)
        {
            grid.RowLocked = reloaded.RowLocked;
            OnPropertyChanged(nameof(CurrentGrid));
        }
        StatusMessage = afterRow[rowIndex] ? $"行 {rowIndex} をロックしました。" : $"行 {rowIndex} のロックを解除しました。";
        return true;
    }

    // ─── 配置ファースト UI 第 2 段階 (Stage 2): バリアント新規作成 / インラインリネーム / 削除 ───
    //
    // 配置タブの候補リストから直接バリアントを管理できるようにする。準備タブ
    // (CopyListViewModel) で行っていた操作と意味的に同じだが、候補リストは
    // 「全アセットのバリアントをフラット表示」なので、操作対象は SelectedCandidate を
    // 起点とする。CopyLibraryChangedMessage で他 VM (CopyList 等) と同期する。

    /// <summary>
    /// 「+ 新規バリアント」フライアウトを開く。<see cref="DraftVariantName"/> を空にリセットして、
    /// View 側で名前入力 TextBox を表示する。<see cref="SelectedCandidate"/> 未選択 / IsBusy 中は no-op。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBeginCreateVariant))]
    public void BeginCreateVariant()
    {
        if (SelectedCandidate is null || IsBusy) return;
        DraftVariantName = string.Empty;
        IsCreatingVariant = true;
    }

    private bool CanBeginCreateVariant() => SelectedCandidate is not null && !IsBusy;

    /// <summary>新規作成フライアウトを閉じる（作成しない）。</summary>
    [RelayCommand]
    public void CancelCreateVariant()
    {
        IsCreatingVariant = false;
        DraftVariantName = string.Empty;
    }

    /// <summary>
    /// 新規作成フライアウトの確定。<see cref="DraftVariantName"/> を渡して
    /// <see cref="CreateLogicalCopyUseCase"/> を呼び、<see cref="SelectedCandidate"/> の
    /// アセットに紐づく新バリアントを生成する。空白 / 空文字なら「バリアント N」自動採番。
    /// 新規 Copy 作成は Undo 対象外（履歴に積めないので _history.Clear()）。
    /// </summary>
    [RelayCommand]
    public async Task CommitCreateVariantAsync(CancellationToken ct = default)
    {
        var candidate = SelectedCandidate;
        if (candidate is null || IsBusy)
        {
            IsCreatingVariant = false;
            DraftVariantName = string.Empty;
            return;
        }

        try
        {
            IsBusy = true;
            var assetId = candidate.AssetId;
            // 命名規則は CopyListViewModel と揃える: 同じアセットに紐づく既存バリアント数 + 1
            var ordinal = Candidates.Count(c => c.AssetId == assetId) + 1;
            var nameToUse = string.IsNullOrWhiteSpace(DraftVariantName)
                ? $"{Terminology.VariantPrefix} {ordinal}"
                : DraftVariantName.Trim();

            var result = await _createCopyUseCase.ExecuteAsync(assetId, copyName: nameToUse, ct: ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            StatusMessage = $"「{nameToUse}」を作成しました。";
            // 新規 Copy 作成は Undo 対象外。既存履歴の参照整合性が崩れる前にクリア。
            _history.Clear();
            _messenger.Send(new CopyLibraryChangedMessage());
            // 新バリアントを選択状態にするため、Candidates 再ロード後に CopyId で再選択。
            // ReloadFromMessageAsync は fire-and-forget なので await できないが、
            // 自身が受信側でもあるため Receive → ReloadFromMessageAsync の経路で更新される。
            await LoadCandidatesAsync(ct);
            SelectedCandidate = Candidates.FirstOrDefault(c => c.CopyId == result.Value.Id) ?? SelectedCandidate;
            LogVariantCreated(_logger, assetId, result.Value.Id);
        }
        finally
        {
            IsBusy = false;
            IsCreatingVariant = false;
            DraftVariantName = string.Empty;
        }
    }

    /// <summary>
    /// 選択中バリアントを削除する。Cascade で関連 Placement も消えるため、履歴は全消去。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCandidate))]
    public async Task DeleteSelectedCandidateAsync(CancellationToken ct = default)
    {
        var target = SelectedCandidate;
        if (target is null || IsBusy) return;

        try
        {
            IsBusy = true;
            var result = await _copyRepository.DeleteAsync(target.CopyId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            var label = target.CopyDisplayName;
            Candidates.Remove(target);
            // CandidateGroups からも対応 Variant を除去。グループが空になったらグループごと削除。
            var group = CandidateGroups.FirstOrDefault(g => g.AssetId == target.AssetId);
            if (group is not null)
            {
                group.Variants.Remove(target);
                if (group.Variants.Count == 0)
                    CandidateGroups.Remove(group);
            }
            SelectedCandidate = Candidates.FirstOrDefault();

            StatusMessage = $"「{label}」を削除しました。";
            // Copy 削除は cascade で Placement も消えるため履歴を全消去
            _history.Clear();
            _messenger.Send(new CopyLibraryChangedMessage());
            LogVariantDeleted(_logger, target.CopyId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDeleteSelectedCandidate() => !IsBusy && SelectedCandidate is not null;

    /// <summary>
    /// インラインリネーム編集を開始する。<paramref name="candidate"/> の <see cref="CopyCandidateViewModel.IsEditing"/>=true、
    /// <see cref="CopyCandidateViewModel.EditingName"/> に現在の <see cref="CopyCandidateViewModel.CopyName"/> をコピー。
    /// 同時に他項目が編集中なら強制的にキャンセルする（同時編集を防ぐ）。
    /// </summary>
    public void BeginEditCandidate(CopyCandidateViewModel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        foreach (var c in Candidates)
        {
            if (!ReferenceEquals(c, candidate) && c.IsEditing)
            {
                c.IsEditing = false;
                c.EditingName = null;
            }
        }
        candidate.EditingName = candidate.CopyName;
        candidate.IsEditing = true;
    }

    /// <summary>インラインリネーム編集をキャンセル（保存しない）。</summary>
    public void CancelEditCandidate(CopyCandidateViewModel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.IsEditing = false;
        candidate.EditingName = null;
        LogVariantRenameCanceled(_logger, candidate.CopyId);
    }

    /// <summary>
    /// インラインリネームを確定して DB に保存する。<see cref="CopyCandidateViewModel.EditingName"/> を
    /// trim（空白だけなら null）した上で <see cref="CopyCandidateViewModel.CopyName"/> と比較し、
    /// 同じなら no-op、違えば <see cref="UpdateImageCopyCommand"/> を組み立てて履歴に積む。
    /// Undo/Redo round-trip 対応（CopyListViewModel.CommitEditAsync と同パターン）。
    /// </summary>
    public async Task CommitEditCandidateAsync(CopyCandidateViewModel candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsEditing) return;

        var trimmed = string.IsNullOrWhiteSpace(candidate.EditingName) ? null : candidate.EditingName!.Trim();
        var beforeName = candidate.CopyName;
        // 編集状態は先に閉じる（保存中の View 再描画で TextBox にフォーカスが残らないように）
        candidate.IsEditing = false;
        candidate.EditingName = null;

        if (string.Equals(trimmed, beforeName, StringComparison.Ordinal))
            return; // 変更なしなら履歴に積まない

        var before = new UpdateImageCopyChanges
        {
            CopyName = beforeName,
            ClearCopyName = beforeName is null,
        };
        var after = new UpdateImageCopyChanges
        {
            CopyName = trimmed,
            ClearCopyName = trimmed is null,
        };
        var beforeLabel = string.IsNullOrWhiteSpace(beforeName) ? Terminology.VariantUnnamed : beforeName!;
        var afterLabel = string.IsNullOrWhiteSpace(trimmed) ? Terminology.VariantUnnamed : trimmed!;
        var description = $"{Terminology.Variant}名変更: 「{beforeLabel}」→「{afterLabel}」";
        var command = new UpdateImageCopyCommand(_updateCopyUseCase, candidate.CopyId, before, after, description);

        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return;
        }

        // 永続化が成功したので VM の表示も即時更新（CopyDisplayName が再計算される）
        candidate.CopyName = trimmed;
        _messenger.Send(new CopyLibraryChangedMessage());
        LogVariantRenamed(_logger, candidate.CopyId);
    }

    /// <summary>
    /// SelectedCandidate / IsBusy / IsCreatingVariant 変化時に
    /// 関連コマンドの CanExecute を再評価する。
    /// </summary>
    partial void OnSelectedCandidateChanged(CopyCandidateViewModel? value)
    {
        BeginCreateVariantCommand.NotifyCanExecuteChanged();
        DeleteSelectedCandidateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        BeginCreateVariantCommand.NotifyCanExecuteChanged();
        DeleteSelectedCandidateCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "配置候補を読み込み: {Count} 件")]
    private static partial void LogCandidatesLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "配置を読み込み: grid={GridId} count={Count}")]
    private static partial void LogPlacementsLoaded(ILogger logger, Guid gridId, int count);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Warning, Message = "候補・配置の自動更新に失敗")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information, Message = "配置タブから新規バリアント作成: asset={AssetId} copy={CopyId}")]
    private static partial void LogVariantCreated(ILogger logger, Guid assetId, Guid copyId);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Information, Message = "配置タブからバリアント削除: copy={CopyId}")]
    private static partial void LogVariantDeleted(ILogger logger, Guid copyId);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Information, Message = "配置タブからバリアントをリネーム: copy={CopyId}")]
    private static partial void LogVariantRenamed(ILogger logger, Guid copyId);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Debug, Message = "配置タブからバリアントのリネームをキャンセル: copy={CopyId}")]
    private static partial void LogVariantRenameCanceled(ILogger logger, Guid copyId);
}
