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
using ViewGrid.Application.Messages;

namespace ViewGrid.Application.ViewModels;

public sealed partial class MainWindowViewModel
    : ViewModelBase, IDisposable, IRecipient<NavigateToCopyPropertiesMessage>
{
    /// <summary>準備タブのインデックス。<see cref="NavigateAsync"/> でタブを切り替える際に使う。</summary>
    public const int PreparationTabIndex = 0;

    /// <summary>配置タブのインデックス（参考）。</summary>
    public const int LayoutTabIndex = 1;

    private readonly IMessenger _messenger;
    private readonly IUndoRedoService _history;
    /// <summary>
    /// 現在進行中の CopyList ロードを中断するための CTS。
    /// マルチセレクト中の <c>SelectedAssets</c> の Clear/Add 連鎖で本ハンドラが
    /// 何度も発火するため、古い <see cref="CopyListViewModel.LoadForAssetAsync"/>
    /// が後から完了して最終状態と矛盾する CopyList を残すレース条件を防ぐ。
    /// </summary>
    private CancellationTokenSource? _copyLoadCts;

    /// <summary>
    /// CopyList ロードを直列化するためのセマフォ。CancellationToken は EF Core 側で
    /// 即座に効くとは限らず、共有 <c>ViewGridDbContext</c> で同時クエリが走ると
    /// 「A second operation was started on this context」例外を投げるため、
    /// 物理的に重ならないよう gate で順序制御する（1 つずつ実行）。
    /// </summary>
    private readonly SemaphoreSlim _copyLoadGate = new(1, 1);

    /// <summary>
    /// 直近 Undo/Redo で取り消された/再適用された Command の <see cref="IUndoableCommand.AffectedGridId"/>。
    /// <see cref="IUndoRedoService.Undone"/> / <see cref="IUndoRedoService.Redone"/> ハンドラ内で退避し、
    /// <see cref="RefreshAfterHistoryAsync"/> 末尾でアクティブグリッド切替に使ったあとに <c>null</c> へ戻す。
    /// グリッドに紐付かない操作（共有コピー編集など）は <c>null</c> なので切替対象外。
    /// </summary>
    private Guid? _pendingAffectedGridId;

    [ObservableProperty]
    public partial string Title { get; set; } = "ViewGrid";

    /// <summary>現在のタブインデックス（0: 準備、1: 配置）。</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>タブ切替時にステータスバーの表示を更新する。</summary>
    partial void OnSelectedTabIndexChanged(int value) => OnPropertyChanged(nameof(StatusSummary));

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
    /// ステータスバー（最下部）の左側に表示する、現在のフェーズ + 件数の要約。
    /// 例: 「準備: 12 件のアセット / 3 件のコピー」「配置: 5/9 セル使用」。
    /// 派生プロパティ（再計算は OnAssetLibraryPropertyChanged / OnCopyListPropertyChanged 等から
    /// 通知される）。<see cref="StatusSummary"/> プロパティ経由で View にバインド。
    /// </summary>
    public string StatusSummary
    {
        get
        {
            if (SelectedTabIndex == PreparationTabIndex)
            {
                var assetCount = AssetLibrary.Assets.Count;
                var copyCount = CopyList.Copies.Count;
                var assetText = AssetLibrary.SelectedAsset is null
                    ? $"{assetCount} 件のアセット"
                    : $"{assetCount} 件のアセット / 選択中 1 件";
                return copyCount > 0
                    ? $"準備: {assetText} / コピー {copyCount} 件"
                    : $"準備: {assetText}";
            }
            else
            {
                var grid = GridList.SelectedGrid;
                if (grid is null)
                    return "配置: グリッド未選択";
                return $"配置: {grid.Name} ({grid.Cols}×{grid.Rows} セル)";
            }
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
    public CopyListViewModel CopyList { get; }
    public CopyPropertiesViewModel CopyProperties { get; }
    public GridCanvasListViewModel GridList { get; }
    public GridWorkspaceViewModel GridWorkspace { get; }

    public MainWindowViewModel(
        AssetLibraryViewModel assetLibrary,
        CopyListViewModel copyList,
        CopyPropertiesViewModel copyProperties,
        GridCanvasListViewModel gridList,
        GridWorkspaceViewModel gridWorkspace,
        IMessenger messenger,
        IUndoRedoService history)
    {
        AssetLibrary = assetLibrary;
        CopyList = copyList;
        CopyProperties = copyProperties;
        GridList = gridList;
        GridWorkspace = gridWorkspace;
        _messenger = messenger;
        _history = history;

        AssetLibrary.PropertyChanged += OnAssetLibraryPropertyChanged;
        CopyList.PropertyChanged += OnCopyListPropertyChanged;
        GridList.PropertyChanged += OnGridListPropertyChanged;

        _history.StateChanged += OnHistoryStateChanged;
        _history.Undone += OnHistoryUndoneOrRedone;
        _history.Redone += OnHistoryUndoneOrRedone;
        OnHistoryStateChanged();

        _messenger.Register(this);
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
    ///         サイドバーに反映する。</item>
    ///   <item><see cref="CopyListViewModel.LoadForAssetAsync"/>: <c>UpdateImageCopyCommand</c> の
    ///         取り消し結果（CopyName/Rotation/特性 全般）を準備タブの一覧に反映する。
    ///         <c>CopyPropertiesViewModel.SaveAsync</c> 内で書き込んだ <c>_source</c> の
    ///         スタンプ値が DB ロールバックと乖離する問題をここで解消する。</item>
    ///   <item><see cref="CopyLibraryChangedMessage"/>: <c>GridWorkspaceViewModel</c> 受信側で
    ///         配置タブの候補・配置を再ロードする（<c>PlaceCommand</c> 等の取消結果を反映）。</item>
    /// </list>
    /// </summary>
    private async Task RefreshAfterHistoryAsync(CancellationToken ct)
    {
        await GridList.LoadAsync(ct);

        var asset = AssetLibrary.SelectedAsset;
        if (asset is not null)
        {
            // 自動ロード（OnAssetLibraryPropertyChanged）と同じ _copyLoadGate を経由して
            // 共有 ViewGridDbContext での同時クエリ例外を防ぐ。
            try
            {
                await _copyLoadGate.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                // ユーザーが先頭以外のコピーを編集中のときに、LoadForAssetAsync が
                // SelectedCopy を先頭に強制リセットする副作用がある。Undo で「編集を取り消す」
                // のに選択まで動くと UX 上違和感があるため、ロード前の CopyId を控えて、
                // 同じ Id がロード後の一覧に残っていれば再選択する。
                var previousCopyId = CopyList.SelectedCopy?.CopyId;
                await CopyList.LoadForAssetAsync(asset.AssetId, ct);
                if (previousCopyId is { } id)
                {
                    var restored = CopyList.Copies.FirstOrDefault(c => c.CopyId == id);
                    if (restored is not null)
                        CopyList.SelectedCopy = restored;
                }
            }
            finally
            {
                _copyLoadGate.Release();
            }
        }

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

    /// <summary>
    /// 配置タブの Inspector から「特性を編集 →」で送られてくるナビゲーション要求を処理する。
    /// async void にしないために <see cref="NavigateAsync"/> をテスト用 internal で公開し、
    /// Receive 自体は fire-and-forget で起動する（テストではこの経路を使わない）。
    /// </summary>
    public void Receive(NavigateToCopyPropertiesMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _ = NavigateAsync(message.AssetId, message.CopyId);
    }

    /// <summary>
    /// 準備タブに切り替え、指定アセット + 指定コピーを単一選択にする。
    /// <para>
    /// <see cref="AssetLibraryViewModel.SelectedAsset"/> /
    /// <see cref="AssetLibraryViewModel.SelectedAssets"/> の更新で
    /// <see cref="OnAssetLibraryPropertyChanged"/> が起動し、<see cref="_copyLoadGate"/> 経由で
    /// <see cref="CopyListViewModel.LoadForAssetAsync"/> が実行される。NavigateAsync 自身は
    /// **直接ロードを行わず**、同じ gate を <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>
    /// で待つことで自動ロードの完了後に <see cref="CopyListViewModel.SelectedCopy"/> を設定する。
    /// これにより共有 <c>ViewGridDbContext</c> での同時クエリ例外
    /// （"A second operation was started on this context"）を確実に回避する。
    /// </para>
    /// </summary>
    public async Task NavigateAsync(Guid assetId, Guid copyId, CancellationToken ct = default)
    {
        SelectedTabIndex = PreparationTabIndex;

        var asset = AssetLibrary.Assets.FirstOrDefault(a => a.AssetId == assetId);
        if (asset is null) return;

        // SelectedAssets 経由でマルチセレクト集合も同期（既存ハンドラの選択経路と一致させる）。
        // この 2 行で OnAssetLibraryPropertyChanged が 2 回起動するが、内部の
        // _copyLoadCts.Cancel + 新 cts + gate 待機によって最後の起動だけが実ロードを行う。
        AssetLibrary.UpdateSelectedAssets(new[] { asset });
        AssetLibrary.SelectedAsset = asset;

        // 自動ロードの完了を待つ。OnAssetLibraryPropertyChanged は WaitAsync を先に呼び出し済みのため、
        // SemaphoreSlim の順序により自動ロード → NavigateAsync のこの待機 → 解放、の順で進む。
        try
        {
            await _copyLoadGate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var copy = CopyList.Copies.FirstOrDefault(c => c.CopyId == copyId);
            if (copy is null) return;

            CopyList.UpdateSelectedCopies(new[] { copy });
            CopyList.SelectedCopy = copy;
        }
        finally
        {
            _copyLoadGate.Release();
        }
    }

    private async void OnAssetLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IsBusy が false に戻った時点で最終状態を 1 回反映（削除ループ等の終了後）
        var isBusyTransition = e.PropertyName == nameof(AssetLibraryViewModel.IsBusy);
        if (isBusyTransition)
        {
            if (AssetLibrary.IsBusy) return; // busy 突入は無視、解除タイミングで反映
        }
        else if (e.PropertyName != nameof(AssetLibraryViewModel.SelectedAsset)
                 && e.PropertyName != nameof(AssetLibraryViewModel.SelectedAssets))
        {
            return;
        }

        // 削除ループ等で busy 中の選択変動による LoadForAssetAsync 連鎖発火を抑止。
        // busy 解除時に最終状態で改めてロードされる（上の isBusyTransition 経路）。
        if (AssetLibrary.IsBusy) return;

        // 古いロードはキャンセルし、前のロードの完了を待ってから新しいロードを開始する
        // （EF Core の同時クエリ例外回避 + 最終状態のみ反映）。
        _copyLoadCts?.Cancel();
        _copyLoadCts?.Dispose();
        _copyLoadCts = new CancellationTokenSource();
        var ct = _copyLoadCts.Token;

        try
        {
            await _copyLoadGate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return; // 待っている間に新しい変更が来てキャンセルされた
        }

        try
        {
            if (ct.IsCancellationRequested) return;
            // マルチセレクト中は CopyList をクリア（編集対象を 1 件に絞れない）
            var assetId = AssetLibrary.SelectedAssets.Count > 1
                ? null
                : AssetLibrary.SelectedAsset?.AssetId;
            await CopyList.LoadForAssetAsync(assetId, ct);
        }
        catch (OperationCanceledException)
        {
            // 後続のハンドラに置き換えられたケース。最新の load が最終状態を反映するので無視。
        }
        finally
        {
            _copyLoadGate.Release();
        }
    }

    private void OnCopyListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isBusyTransition = e.PropertyName == nameof(CopyListViewModel.IsBusy);
        if (isBusyTransition)
        {
            if (CopyList.IsBusy) return;
        }
        else if (e.PropertyName != nameof(CopyListViewModel.SelectedCopy)
                 && e.PropertyName != nameof(CopyListViewModel.SelectedCopies))
        {
            return;
        }

        // 削除/ロード中の連鎖選択変動による Attach 呼び出しを抑止
        if (CopyList.IsBusy) return;

        // マルチセレクト中は特性編集を disabled（Attach(null) で HasCopy=false）にし、
        // 件数を案内文として表示する
        if (CopyList.SelectedCopies.Count > 1)
        {
            CopyProperties.Attach(null);
            CopyProperties.MultiSelectMessage = $"{CopyList.SelectedCopies.Count} 件選択中（編集は 1 件のみ）";
        }
        else
        {
            CopyProperties.MultiSelectMessage = null;
            CopyProperties.Attach(CopyList.SelectedCopy);
        }
    }

    private async void OnGridListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GridCanvasListViewModel.SelectedGrid))
            return;

        await GridWorkspace.LoadGridAsync(GridList.SelectedGrid);
    }

    public void Dispose()
    {
        _messenger.UnregisterAll(this);
        _history.StateChanged -= OnHistoryStateChanged;
        _history.Undone -= OnHistoryUndoneOrRedone;
        _history.Redone -= OnHistoryUndoneOrRedone;
        _copyLoadCts?.Cancel();
        _copyLoadCts?.Dispose();
        _copyLoadCts = null;
        _copyLoadGate.Dispose();
    }
}
