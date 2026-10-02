using ViewGrid.Core.Services;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// 回答をテストが後から返す <see cref="IConfirmationService"/>。 実際のダイアログと同じく、 問い合わせてから
/// 回答が来るまでの間 (= 利用者が考えている間) に他の操作・非同期処理が進む状況を再現する。
/// 即答する <see cref="AutoConfirmationService"/> では、 確認の前後で起きる状態の食い違いを再現できない。
/// </summary>
internal sealed class DeferredConfirmationService : IConfirmationService
{
    private readonly Queue<TaskCompletionSource<UnsavedChoice>> _unsaved = new();
    private readonly Queue<TaskCompletionSource<bool>> _confirm = new();

    /// <summary>これまでの未保存の編集に関する問い合わせの数。</summary>
    public int UnsavedRequests { get; private set; }

    /// <summary>未保存の編集の問い合わせに、 回答待ちのものがあるか。</summary>
    public bool HasPendingUnsavedRequest => _unsaved.Count > 0;

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirm.Enqueue(tcs);
        return tcs.Task;
    }

    public Task<UnsavedChoice> AskUnsavedChangesAsync(
        string title, string message, string saveLabel, string discardLabel, string cancelLabel,
        CancellationToken ct = default)
    {
        UnsavedRequests++;
        var tcs = new TaskCompletionSource<UnsavedChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _unsaved.Enqueue(tcs);
        return tcs.Task;
    }

    /// <summary>最も古い回答待ちの問い合わせへ回答する。</summary>
    public void Answer(UnsavedChoice choice) => _unsaved.Dequeue().SetResult(choice);

    /// <summary>最も古い回答待ちの確認 (削除など) へ回答する。 実行するなら <c>true</c>。</summary>
    public void AnswerConfirm(bool confirmed) => _confirm.Dequeue().SetResult(confirmed);

    /// <summary>確認 (削除など) が来る (= 回答待ちになる) まで待つ。</summary>
    public async Task WaitForConfirmRequestAsync(int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (_confirm.Count == 0)
        {
            if (Environment.TickCount64 > until)
                throw new TimeoutException("確認が要求されませんでした。");
            await Task.Delay(10);
        }
    }

    /// <summary>問い合わせが来る (= 回答待ちになる) まで待つ。</summary>
    public async Task WaitForUnsavedRequestAsync(int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!HasPendingUnsavedRequest)
        {
            if (Environment.TickCount64 > until)
                throw new TimeoutException("未保存の編集の確認が要求されませんでした。");
            await Task.Delay(10);
        }
    }
}
