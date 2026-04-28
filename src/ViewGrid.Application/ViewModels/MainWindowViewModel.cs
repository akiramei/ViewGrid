using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
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

    [ObservableProperty]
    public partial string Title { get; set; } = "ViewGrid";

    /// <summary>現在のタブインデックス（0: 準備、1: 配置）。</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

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
        IMessenger messenger)
    {
        AssetLibrary = assetLibrary;
        CopyList = copyList;
        CopyProperties = copyProperties;
        GridList = gridList;
        GridWorkspace = gridWorkspace;
        _messenger = messenger;

        AssetLibrary.PropertyChanged += OnAssetLibraryPropertyChanged;
        CopyList.PropertyChanged += OnCopyListPropertyChanged;
        GridList.PropertyChanged += OnGridListPropertyChanged;

        _messenger.Register(this);
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
        _copyLoadCts?.Cancel();
        _copyLoadCts?.Dispose();
        _copyLoadCts = null;
        _copyLoadGate.Dispose();
    }
}
