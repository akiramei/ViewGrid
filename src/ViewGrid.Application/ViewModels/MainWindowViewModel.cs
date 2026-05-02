using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ViewGrid.Application.History;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;

namespace ViewGrid.Application.ViewModels;

public sealed partial class MainWindowViewModel
    : ViewModelBase, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly IUndoRedoService _history;

    /// <summary>
    /// 直近 Undo/Redo で取り消された/再適用された Command の <see cref="IUndoableCommand.AffectedGridId"/>。
    /// <see cref="IUndoRedoService.Undone"/> / <see cref="IUndoRedoService.Redone"/> ハンドラ内で退避し、
    /// <see cref="RefreshAfterHistoryAsync"/> 末尾でアクティブグリッド切替に使ったあとに <c>null</c> へ戻す。
    /// グリッドに紐付かない操作（共有コピー編集など）は <c>null</c> なので切替対象外。
    /// </summary>
    private Guid? _pendingAffectedGridId;

    [ObservableProperty]
    public partial string Title { get; set; } = "ViewGrid";

    /// <summary>Undo 可能な操作があるか。<see cref="UndoCommand"/> の CanExecute と連動する。</summary>
    [ObservableProperty]
    public partial bool CanUndo { get; set; }

    /// <summary>Redo 可能な操作があるか。<see cref="RedoCommand"/> の CanExecute と連動する。</summary>
    [ObservableProperty]
    public partial bool CanRedo { get; set; }

    /// <summary>編集メニューに表示する Undo ラベル（例: "元に戻す: 配置"）。</summary>
    [ObservableProperty]
    public partial string UndoLabel { get; set; } = "元に戻す";

    /// <summary>編集メニューに表示する Redo ラベル（例: "やり直し: 配置"）。</summary>
    [ObservableProperty]
    public partial string RedoLabel { get; set; } = "やり直し";

    /// <summary>履歴 UI（Phase 2 のツールバー Flyout）にバインドする履歴エントリ一覧。
    /// <see cref="IUndoRedoService.History"/> を都度評価する（StateChanged で通知）。</summary>
    public IReadOnlyList<HistoryEntry> HistoryEntries => _history.History;

    /// <summary>履歴 UI で選択状態の表示に使う「現在位置」インデックス。
    /// <see cref="IUndoRedoService.CurrentIndex"/> を都度評価する。</summary>
    public int CurrentHistoryIndex => _history.CurrentIndex;

    /// <summary>
    /// ステータスバー（最下部）の左側に表示する要約。配置ファースト UI 第 2 段階 Stage 4 で
    /// 準備タブが廃止されたため、常に「アセット件数 + 現在のグリッド情報」を表示する。
    /// 派生プロパティ（AssetLibrary.Assets / GridList.SelectedGrid 変化で再評価される）。
    /// </summary>
    public string StatusSummary
    {
        get
        {
            var assetCount = AssetLibrary.Assets.Count;
            var grid = GridList.SelectedGrid;
            if (grid is null)
                return $"アセット {assetCount} 件 / グリッド未選択";
            return $"アセット {assetCount} 件 / {grid.Name} ({grid.Cols}×{grid.Rows} セル)";
        }
    }

    /// <summary>
    /// ステータスバー右側の Undo/Redo 状態表示。
    /// 履歴件数 + 現在位置を「履歴: 5/12」形式で示す（履歴空のときは「履歴なし」）。
    /// </summary>
    public string HistorySummary
    {
        get
        {
            var total = _history.History.Count;
            if (total == 0) return "履歴なし";
            var current = _history.CurrentIndex + 1; // 0 始まりを 1 始まりへ
            return $"履歴: {current}/{total}";
        }
    }

    /// <summary>履歴 UI から見て、何かしらエントリがあるか。プレースホルダ表示用。</summary>
    public bool HasHistory => _history.History.Count > 0;

    /// <summary>
    /// アプリ全体で未保存の編集があるか。Stage 3 で Inspector に保存ロジックが統合された
    /// ため、Inspector.IsAnyDirty (= 配置固有 IsDirty || 共有特性 CopyProperties.IsDirty)
    /// を直接見る形に簡素化された。
    /// </summary>
    public bool HasUnsavedChanges => GridWorkspace.Inspector.IsAnyDirty;

    /// <summary>
    /// ステータスバー右側に表示する未保存バッジの文字列。
    /// 未保存なしのときは空文字（View 側で <see cref="HasUnsavedChanges"/> により非表示にする）。
    /// </summary>
    public string UnsavedSummary => HasUnsavedChanges ? "● 未保存の変更" : string.Empty;

    /// <summary>
    /// ステータスバー上のコンテキスト依存ヒント（永続ヒントバー）。
    /// 配置ファースト UI 第 2 段階 Stage 4 で準備タブが廃止されたため、配置ワークスペース内の
    /// 状態（アセット件数 / 選択グリッド / 選択配置 / 選択候補）でヒントを出し分ける。
    /// </summary>
    public string CurrentHints
    {
        get
        {
            if (AssetLibrary.Assets.Count == 0)
                return "画像をドラッグ&ドロップ または「+ 画像を追加」ボタンから取り込みを開始します";
            if (GridList.SelectedGrid is null)
                return "上の「+ 新規」でグリッドを作成すると配置を始められます";
            if (GridWorkspace.SelectedPlacement is not null)
                return "Shift+ドラッグ で微調整 / Ctrl+矢印 1px / Ctrl+Shift+矢印 10px / 境界をドラッグで比率調整";
            if (GridWorkspace.SelectedCandidate is not null)
                return "F2 でバリアント名変更 / Enter で確定 / Esc でキャンセル / セルへ D&D で配置";
            return "右の候補をセルへドラッグ&ドロップで配置 / 境界ドラッグで比率 / ヘッダ 🔒 で固定";
        }
    }

    /// <summary>
    /// 履歴 UI で hover 中のエントリの Index。<c>null</c> なら hover 解除中。
    /// View 側（CodeBehind）が ListBoxItem の <c>PointerEntered</c> / <c>PointerExited</c> で
    /// 設定し、Phase 3 の hover プレビュー（範囲色付け）に使う。
    /// 値変更時に <see cref="UpdateHoveredJumpRange"/> 経由で派生プロパティが再計算される。
    /// </summary>
    [ObservableProperty]
    public partial int? HoveredHistoryIndex { get; set; }

    /// <summary>
    /// hover プレビューで「ジャンプの影響を受ける範囲」の下端 Index（含む）。
    /// 範囲なしのとき <c>-1</c>。Converter で各 HistoryEntry.Index と比較して範囲内判定に使う。
    /// </summary>
    [ObservableProperty]
    public partial int HoveredJumpRangeLo { get; set; } = -1;

    /// <summary>hover ジャンプ範囲の上端 Index（含む）。範囲なしのとき <c>-1</c>。</summary>
    [ObservableProperty]
    public partial int HoveredJumpRangeHi { get; set; } = -1;

    /// <summary>hover が指す方向（Undo / Redo / None）。Converter で背景色の使い分けに使う。</summary>
    [ObservableProperty]
    public partial JumpDirection HoveredJumpDirection { get; set; }

    public AssetLibraryViewModel AssetLibrary { get; }
    public GridCanvasListViewModel GridList { get; }
    public GridWorkspaceViewModel GridWorkspace { get; }

    public MainWindowViewModel(
        AssetLibraryViewModel assetLibrary,
        GridCanvasListViewModel gridList,
        GridWorkspaceViewModel gridWorkspace,
        IMessenger messenger,
        IUndoRedoService history)
    {
        AssetLibrary = assetLibrary;
        GridList = gridList;
        GridWorkspace = gridWorkspace;
        _messenger = messenger;
        _history = history;

        // Stage 4: AssetLibrary は「+ 画像を追加」/ ファイルメニュー経由で件数だけが
        // 変動する。Assets.CollectionChanged で StatusSummary / CurrentHints を更新する。
        AssetLibrary.Assets.CollectionChanged += OnAssetLibraryAssetsChanged;
        GridList.PropertyChanged += OnGridListPropertyChanged;
        GridWorkspace.PropertyChanged += OnGridWorkspacePropertyChanged;
        // Stage 3: Inspector に統合された IsAnyDirty (placement + shared) を未保存バッジに転送
        GridWorkspace.Inspector.PropertyChanged += OnDirtyTrackedPropertyChanged;

        _history.StateChanged += OnHistoryStateChanged;
        _history.Undone += OnHistoryUndoneOrRedone;
        _history.Redone += OnHistoryUndoneOrRedone;
        OnHistoryStateChanged();
    }

    /// <summary>Ctrl+Z / 編集メニュー → Undo。Undo 後に各 VM を最新 DB 状態に再同期する。</summary>
    [RelayCommand(CanExecute = nameof(CanUndoCommand))]
    public async Task UndoAsync(CancellationToken ct = default)
    {
        var result = await _history.UndoAsync(ct);
        if (!result.IsError)
            await RefreshAfterHistoryAsync(ct);
    }

    /// <summary>Ctrl+Y / Ctrl+Shift+Z / 編集メニュー → Redo。Redo 後に各 VM を最新 DB 状態に再同期する。</summary>
    [RelayCommand(CanExecute = nameof(CanRedoCommand))]
    public async Task RedoAsync(CancellationToken ct = default)
    {
        var result = await _history.RedoAsync(ct);
        if (!result.IsError)
            await RefreshAfterHistoryAsync(ct);
    }

    /// <summary>
    /// 履歴 UI からの直接ジャンプ。複数ステップの一括 Undo / Redo を行う。
    /// <paramref name="targetIndex"/> は <see cref="HistoryEntries"/> 内の Index、
    /// または -1（全 Undo 状態）を指定する。範囲外は <see cref="IUndoRedoService.JumpToAsync"/> 側で
    /// validation エラーになる（UI 側で範囲外を選ばせない設計が前提）。
    /// </summary>
    [RelayCommand]
    public async Task JumpToHistoryAsync(int targetIndex, CancellationToken ct = default)
    {
        var result = await _history.JumpToAsync(targetIndex, ct);
        if (!result.IsError)
            await RefreshAfterHistoryAsync(ct);
    }

    /// <summary>
    /// Undo / Redo 直後に DB と VM の整合を取るため、各画面の VM を最新状態に再ロードする。
    /// <list type="bullet">
    ///   <item><see cref="GridCanvasListViewModel.LoadAsync"/>: <c>RenameGridCanvasCommand</c> /
    ///         <c>SetActiveGridCanvasCommand</c> 等の取り消し結果（名前 / IsActive）を
    ///         グリッド切替バーに反映する。</item>
    ///   <item><see cref="CopyLibraryChangedMessage"/>: <c>GridWorkspaceViewModel</c> 受信側で
    ///         配置タブの候補・配置を再ロードする（<c>PlaceCommand</c> 等の取消結果を反映）。</item>
    /// </list>
    /// Stage 4 で準備タブと CopyList の連動は廃止された。Inspector が attached している
    /// ImageCopy の特性は、配置再ロード経路 (CopyLibraryChangedMessage → GridWorkspace
    /// 再ロード → SelectedPlacement 再選択 → Inspector.AttachAsync) で間接的に最新化される。
    /// </summary>
    private async Task RefreshAfterHistoryAsync(CancellationToken ct)
    {
        await GridList.LoadAsync(ct);

        _messenger.Send(new CopyLibraryChangedMessage());

        // Undo/Redo 対象が現在のアクティブグリッドと異なる場合、当該グリッドへ自動切替する。
        // GridList.LoadAsync 後に行うことで、Grids 再構築後の最新インスタンスから検索できる。
        // JumpToAsync 内の連続 Undo/Redo は最後の event のみが反映される（_pendingAffectedGridId の上書き）。
        if (_pendingAffectedGridId is { } gridId)
        {
            _pendingAffectedGridId = null;
            var target = GridList.Grids.FirstOrDefault(g => g.GridId == gridId);
            if (target is not null && !ReferenceEquals(GridList.SelectedGrid, target))
                GridList.SelectedGrid = target;
        }
    }

    private bool CanUndoCommand() => CanUndo;
    private bool CanRedoCommand() => CanRedo;

    private void OnHistoryStateChanged()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
        UndoLabel = _history.NextUndoDescription is { } u ? $"元に戻す: {u}" : "元に戻す";
        RedoLabel = _history.NextRedoDescription is { } r ? $"やり直し: {r}" : "やり直し";

        // 履歴 UI 用プロパティ（History は呼び出しのたびに新しい List を生成するため、
        // 参照変更通知でバインドが再評価される）
        OnPropertyChanged(nameof(HistoryEntries));
        OnPropertyChanged(nameof(CurrentHistoryIndex));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistorySummary));

        // CurrentIndex が変わると hover 範囲の意味も変わる（ジャンプ方向の判定が変わる）。
        // hover 中ならその場で再計算しておく。hover 解除中なら no-op。
        UpdateHoveredJumpRange();

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// <see cref="HoveredHistoryIndex"/> が変わったとき、または現在位置 (
    /// <see cref="CurrentHistoryIndex"/>) が変わったときに呼ばれる。
    /// hover が現在位置より新しければ Redo 方向、古ければ Undo 方向の範囲を計算する。
    /// </summary>
    partial void OnHoveredHistoryIndexChanged(int? value) => UpdateHoveredJumpRange();

    private void UpdateHoveredJumpRange()
    {
        if (HoveredHistoryIndex is not int hover)
        {
            HoveredJumpRangeLo = -1;
            HoveredJumpRangeHi = -1;
            HoveredJumpDirection = JumpDirection.None;
            return;
        }

        var current = CurrentHistoryIndex;
        if (hover == current)
        {
            // 同じ位置 = no-op。プレビュー範囲なし。
            HoveredJumpRangeLo = -1;
            HoveredJumpRangeHi = -1;
            HoveredJumpDirection = JumpDirection.None;
        }
        else if (hover > current)
        {
            // クリックすると [current+1, hover] の取消済みエントリを Redo で再適用する。
            HoveredJumpRangeLo = current + 1;
            HoveredJumpRangeHi = hover;
            HoveredJumpDirection = JumpDirection.Redo;
        }
        else
        {
            // クリックすると [hover+1, current] の適用済みエントリを Undo で取り消す。
            HoveredJumpRangeLo = hover + 1;
            HoveredJumpRangeHi = current;
            HoveredJumpDirection = JumpDirection.Undo;
        }
    }

    private void OnHistoryUndoneOrRedone(IUndoableCommand command)
    {
        // Command 自体は短命で AffectedGridId だけが必要なので、Guid? を退避する。
        // RefreshAfterHistoryAsync 末尾で読み出し → クリアする。
        _pendingAffectedGridId = command.AffectedGridId;
    }

    // 配置ファースト UI 第 2 段階 Stage 4 で準備タブが廃止されたため、
    // NavigateToCopyPropertiesMessage 経由のタブ遷移 (Receive / NavigateAsync) と
    // CopyList ロードの直列化 (_copyLoadCts / _copyLoadGate) は撤去された。
    // 共有特性の編集は Inspector 内 inline embed で完結する。

    private async void OnGridListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GridCanvasListViewModel.SelectedGrid))
            return;

        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(CurrentHints));
        await GridWorkspace.LoadGridAsync(GridList.SelectedGrid);
    }

    /// <summary>
    /// 配置ワークスペース側の <see cref="GridWorkspaceViewModel.SelectedPlacement"/> 変化を
    /// ヒントバーに反映するための購読。配置選択の有無で「微調整 / Shift+ドラッグ」案内を出し分ける。
    /// </summary>
    private void OnGridWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GridWorkspaceViewModel.SelectedPlacement))
            OnPropertyChanged(nameof(CurrentHints));
    }

    /// <summary>
    /// アセット件数変化時にステータスバー / ヒントバーを更新するためのフック。
    /// 0 → 1 件目を取り込んだとき、空状態案内から通常ヒントへ切り替えるのが主目的。
    /// </summary>
    private void OnAssetLibraryAssetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(CurrentHints));
    }

    /// <summary>
    /// <see cref="PlacementInspectorViewModel.IsAnyDirty"/> 変化を未保存バッジへ集約。
    /// Stage 3 で IsAnyDirty = Inspector.IsDirty || CopyProperties.IsDirty が一本化されたため、
    /// MainWindow 側はこのプロパティだけ購読する。
    /// </summary>
    private void OnDirtyTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlacementInspectorViewModel.IsAnyDirty))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(UnsavedSummary));
        }
    }

    public void Dispose()
    {
        _history.StateChanged -= OnHistoryStateChanged;
        _history.Undone -= OnHistoryUndoneOrRedone;
        _history.Redone -= OnHistoryUndoneOrRedone;
        AssetLibrary.Assets.CollectionChanged -= OnAssetLibraryAssetsChanged;
        GridList.PropertyChanged -= OnGridListPropertyChanged;
        GridWorkspace.PropertyChanged -= OnGridWorkspacePropertyChanged;
        GridWorkspace.Inspector.PropertyChanged -= OnDirtyTrackedPropertyChanged;
    }
}
