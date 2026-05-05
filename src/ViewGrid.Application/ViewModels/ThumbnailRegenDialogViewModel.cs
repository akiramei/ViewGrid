using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 全サムネ再生成ダイアログの ViewModel。
/// 「開始」 で <see cref="RegenerateThumbnailsUseCase"/> を <see cref="IProgress{T}"/> 付きで起動し、
/// バックグラウンドで進捗を受け取り、 完了時に再起動を促すヒントを表示する。
/// 「キャンセル」 は CTS.Cancel() で停止 (途中まで生成済みのサムネは残置)。
/// </summary>
public sealed partial class ThumbnailRegenDialogViewModel : ViewModelBase, IDisposable
{
    private readonly RegenerateThumbnailsUseCase _useCase;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    public partial int Total { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    public partial int Completed { get; set; }

    [ObservableProperty] public partial string CurrentAssetName { get; set; } = string.Empty;
    [ObservableProperty] public partial int Successful { get; set; }
    [ObservableProperty] public partial int Skipped { get; set; }
    [ObservableProperty] public partial int Failed { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty] public partial bool IsCancelled { get; set; }

    /// <summary>完了画面で表示するメッセージ (再起動ヒント or キャンセル通知)。</summary>
    [ObservableProperty] public partial string CompletionMessage { get; set; } = string.Empty;

    /// <summary>進捗バーの 0-100 表示用パーセント (Total=0 なら 0)。</summary>
    public int ProgressPercent => Total > 0 ? (int)((double)Completed / Total * 100) : 0;

    /// <summary>
    /// View 側に「Window を閉じてほしい」 通知。 ViewModel から直接 Window を Close する代わりに、
    /// View が購読してハンドルする。
    /// </summary>
    public event EventHandler? CloseRequested;

    public ThumbnailRegenDialogViewModel(RegenerateThumbnailsUseCase useCase)
    {
        _useCase = useCase;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        IsCompleted = false;
        IsCancelled = false;
        Successful = 0;
        Skipped = 0;
        Failed = 0;
        Completed = 0;
        CompletionMessage = string.Empty;

        _cts = new CancellationTokenSource();
        var progress = new Progress<ThumbnailRegenProgress>(OnProgress);
        try
        {
            var result = await _useCase.ExecuteAsync(progress, _cts.Token);
            IsCancelled = result.Cancelled;
            CompletionMessage = result.Cancelled
                ? $"キャンセルされました。 (成功: {result.Successful} / スキップ: {result.Skipped} / 失敗: {result.Failed})"
                : $"再生成が完了しました (成功: {result.Successful} / スキップ: {result.Skipped} / 失敗: {result.Failed})。 反映するにはアプリを再起動してください。";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            IsCompleted = true;
        }
    }

    private bool CanStart() => !IsRunning && !IsCompleted;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private bool CanClose() => !IsRunning;

    /// <summary>
    /// <see cref="Progress{T}"/> の callback。 SynchronizationContext を持つ環境 (= UI スレッド
    /// から作成された場合) では UI スレッド上で呼ばれるので Dispatcher.UIThread.Post 等は不要。
    /// </summary>
    private void OnProgress(ThumbnailRegenProgress p)
    {
        Total = p.Total;
        Completed = p.Completed;
        CurrentAssetName = p.CurrentAssetName;
        Successful = p.Successful;
        Skipped = p.Skipped;
        Failed = p.Failed;
    }

    public void Dispose()
    {
        // ダイアログ閉じる時に進行中の再生成を停止 + CTS 解放 (CA1001 対応)
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
