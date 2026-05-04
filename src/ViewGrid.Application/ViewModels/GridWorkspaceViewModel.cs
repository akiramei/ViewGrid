using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    /// プレビュー / PNG 出力の最上位モード。通常 / 写真ボードを切り替える。
    /// 切り出し (<see cref="SelectedTrimMode"/>) とは直交軸で、両方を組み合わせて使う。
    /// 永続化はせず、セッション内のオプション扱い（既定 Normal）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhotoBoardMode))]
    [NotifyPropertyChangedFor(nameof(IsNormalMode))]
    public partial OutputMode SelectedOutputMode { get; set; } = OutputMode.Normal;

    /// <summary>
    /// プレビュー / PNG 出力の切り出し設定。<see cref="TrimMode.None"/> はキャンバス全面、
    /// <see cref="TrimMode.OccupiedCells"/> は占有セルの bbox で切り出し、
    /// <see cref="TrimMode.DrawnPixels"/> は α&gt;0 のピクセル走査で求めた bbox で切り出し。
    /// PhotoBoard モードでは「合成後の画像」に対して同じセマンティクスで適用される。
    /// 永続化はせず、セッション内のオプション扱い（既定 None）。
    /// </summary>
    [ObservableProperty]
    public partial TrimMode SelectedTrimMode { get; set; } = TrimMode.None;

    public IReadOnlyList<TrimMode> TrimModeOptions { get; } =
        [TrimMode.None, TrimMode.OccupiedCells, TrimMode.DrawnPixels];

    public IReadOnlyList<OutputMode> OutputModeOptions { get; } =
        [OutputMode.Normal, OutputMode.PhotoBoard];

    public IReadOnlyList<PhotoBoardStyle> PhotoBoardStyleOptions { get; } =
        [PhotoBoardStyle.Natural, PhotoBoardStyle.Rough, PhotoBoardStyle.Scattered];

    /// <summary>
    /// PhotoBoard モードのスタイルプリセット。各値は係数セット
    /// (<see cref="PhotoBoardStyleCoefficients"/>) を引くキー。<see cref="OutputMode.Normal"/>
    /// 時は無視される。永続化はせずセッション内のオプション扱い (既定 Natural)。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStyleNatural))]
    [NotifyPropertyChangedFor(nameof(IsStyleRough))]
    [NotifyPropertyChangedFor(nameof(IsStyleScattered))]
    public partial PhotoBoardStyle SelectedPhotoBoardStyle { get; set; } = PhotoBoardStyle.Natural;

    /// <summary>選択中スタイルがナチュラルかどうか (View 側のスタイルボタン IsChecked 表示)。</summary>
    public bool IsStyleNatural => SelectedPhotoBoardStyle == PhotoBoardStyle.Natural;

    /// <summary>選択中スタイルがラフかどうか。</summary>
    public bool IsStyleRough => SelectedPhotoBoardStyle == PhotoBoardStyle.Rough;

    /// <summary>選択中スタイルがバラ撒きかどうか。</summary>
    public bool IsStyleScattered => SelectedPhotoBoardStyle == PhotoBoardStyle.Scattered;

    /// <summary>
    /// PhotoBoard モードの強度。<c>0.0</c> で「ほぼ整列」(係数すべて 0 倍)、
    /// <c>0.5</c> でスタイル基準値そのまま、<c>1.0</c> で「最大効果」(係数 2 倍)。
    /// UI 上では数値非表示で「控えめ ↔ 大胆」の感覚スライダーとして見せる。
    /// 永続化はせずセッション内のオプション扱い (既定 0.5)。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetPhotoBoardIntensityCommand))]
    public partial double SelectedPhotoBoardIntensity { get; set; } = 0.5;

    /// <summary>「既定に戻す」ボタンの活性条件。スライダーが既定値 (0.5) から
    /// 動いていれば <c>true</c>。 既定位置のときは <c>false</c> でボタンが無効表示になる。</summary>
    private bool CanResetPhotoBoardIntensity() =>
        Math.Abs(SelectedPhotoBoardIntensity - 0.5) > 0.001;

    /// <summary>
    /// スタイル切替時は強度を既定値 (0.5 = そのスタイルのベースライン) に戻す。
    /// 各スタイルは <see cref="PhotoBoardStyleCoefficients"/> ベースラインが大きく異なるため、
    /// 同じ intensity でも見え方が大きく変わる。 「スタイルを選んだ直後はそのスタイルの基準値で
    /// 見える」状態に揃えることで、 比較の起点が明確になる UX 契約。
    /// </summary>
    partial void OnSelectedPhotoBoardStyleChanged(PhotoBoardStyle value) =>
        SelectedPhotoBoardIntensity = 0.5;

    [RelayCommand]
    private void SelectOutputModeNormal() => SelectedOutputMode = OutputMode.Normal;

    [RelayCommand]
    private void SelectOutputModePhotoBoard() => SelectedOutputMode = OutputMode.PhotoBoard;

    [RelayCommand]
    private void ApplyPhotoBoardStyleNatural() => SelectedPhotoBoardStyle = PhotoBoardStyle.Natural;

    [RelayCommand]
    private void ApplyPhotoBoardStyleRough() => SelectedPhotoBoardStyle = PhotoBoardStyle.Rough;

    [RelayCommand]
    private void ApplyPhotoBoardStyleScattered() => SelectedPhotoBoardStyle = PhotoBoardStyle.Scattered;

    /// <summary>「配置の乱れ」スライダーを既定値 (0.5) に戻す。スライダーは正確に
    /// 中央へ戻すのが難しい UI のため、明示的なリセット手段を提供する。
    /// 既定位置にあるときは <see cref="CanResetPhotoBoardIntensity"/> で無効化される。</summary>
    [RelayCommand(CanExecute = nameof(CanResetPhotoBoardIntensity))]
    private void ResetPhotoBoardIntensity() => SelectedPhotoBoardIntensity = 0.5;

    /// <summary>
    /// <see cref="SelectedOutputMode"/> が <see cref="OutputMode.PhotoBoard"/> のときに <c>true</c>。
    /// View 側でスタイル / 強度パネルの表示切替に使う。
    /// </summary>
    public bool IsPhotoBoardMode => SelectedOutputMode == OutputMode.PhotoBoard;

    /// <summary>
    /// <see cref="SelectedOutputMode"/> が <see cref="OutputMode.Normal"/> のときに <c>true</c>。
    /// 出力モードの ToggleButton ペアの IsChecked 表示に使う (PhotoBoard モードと排他)。
    /// </summary>
    public bool IsNormalMode => SelectedOutputMode == OutputMode.Normal;

    /// <summary>
    /// 現在の VM 設定からレンダリングオプションを構築する。 PhotoBoard モード時は係数を
    /// スタイル + 強度から派生させる。 Normal 時は coefs=null。
    /// </summary>
    private RenderOptions BuildRenderOptions()
    {
        var coefs = SelectedOutputMode == OutputMode.PhotoBoard
            ? PhotoBoardStyleCoefficients.For(SelectedPhotoBoardStyle, SelectedPhotoBoardIntensity)
            : null;
        return new RenderOptions(
            TrimMode: SelectedTrimMode,
            OutputMode: SelectedOutputMode,
            PhotoBoardCoefficients: coefs);
    }

    /// <summary>
    /// 「+ 新規バリアント」フライアウトを開いているか。<c>true</c> の間だけ View 側で名前入力 TextBox と
    /// 確定/キャンセルボタンが表示される（<see cref="GridCanvasListViewModel.IsCreating"/> と同パターン）。
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

            // 局所追加: 新規 1 件だけ取得して Placements に Add する。
            // 全件再ロードは不要（既存配置は何も変わらない）。
            var added = command.CreatedPlacementId is { } pid
                ? await AppendPlacementToViewAsync(pid, ct)
                : null;
            SelectedPlacement = added;
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

            // 局所追加: 新規 1 件のみ取得して Placements に Add する。
            var added = command.CreatedPlacementId is { } pid
                ? await AppendPlacementToViewAsync(pid, ct)
                : null;
            SelectedPlacement = added;
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

    /// <summary>
    /// 現在のグリッドをレンダリングして PNG バイト列を返す（プレビュー用）。
    /// 失敗時は <c>null</c> を返し、<see cref="StatusMessage"/> にエラーを格納する。
    /// </summary>
    public async Task<byte[]?> RequestPreviewAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return null;

        var sw = Stopwatch.StartNew();
        try
        {
            IsBusy = true;
            var options = BuildRenderOptions();
            var result = await _renderUseCase.ExecuteAsync(grid.GridId, options, ct);
            sw.Stop();
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return null;
            }
            StatusMessage = $"プレビュー生成 {sw.ElapsedMilliseconds:N0} ms ({result.Value.Length:N0} bytes)";
            LogPreviewRendered(_logger, options.TrimMode, options.OutputMode, sw.ElapsedMilliseconds, result.Value.Length);
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

        var sw = Stopwatch.StartNew();
        try
        {
            IsBusy = true;
            var options = BuildRenderOptions();
            var result = await _exportUseCase.ExecuteAsync(grid.GridId, path, options, ct);
            sw.Stop();
            StatusMessage = result.IsError
                ? string.Join(", ", result.Errors)
                : $"出力 {sw.ElapsedMilliseconds:N0} ms: {Path.GetFileName(path)} ({result.Value.FileSizeBytes:N0} bytes)";
            if (!result.IsError)
                LogPngExported(_logger, options.TrimMode, options.OutputMode, sw.ElapsedMilliseconds, result.Value.FileSizeBytes);
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
    // 配置タブの候補リストから直接バリアントを管理できるようにする。配置タブが
    // 「全アセットのバリアントをツリー表示」する設計なので、操作対象は SelectedCandidate を
    // 起点とする。CopyLibraryChangedMessage で他 VM と同期する。

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
            // 命名規則: 同じアセットに紐づく既存バリアント数 + 1
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
    /// Undo/Redo round-trip 対応。
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

    [LoggerMessage(EventId = 5008, Level = LogLevel.Information, Message = "プレビュー生成: trim={TrimMode} output={OutputMode} elapsed={ElapsedMs}ms bytes={Bytes}")]
    private static partial void LogPreviewRendered(ILogger logger, TrimMode trimMode, OutputMode outputMode, long elapsedMs, int bytes);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Information, Message = "PNG 出力: trim={TrimMode} output={OutputMode} elapsed={ElapsedMs}ms bytes={Bytes}")]
    private static partial void LogPngExported(ILogger logger, TrimMode trimMode, OutputMode outputMode, long elapsedMs, long bytes);
}
