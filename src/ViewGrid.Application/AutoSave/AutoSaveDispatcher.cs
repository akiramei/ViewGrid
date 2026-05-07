namespace ViewGrid.Application.AutoSave;

/// <summary>
/// IsDirty 立ち上がりから一定時間 (debounce) 静止してから 1 度だけ <c>saveAction</c> を実行する。
/// 連続編集 → 1 回の保存にまとめる用途。
/// </summary>
/// <remarks>
/// <para><b>同時実行防止 (重要)</b>:</para>
/// <list type="bullet">
///   <item>内部 <see cref="SemaphoreSlim"/> で <c>saveAction</c> の実行を直列化する。</item>
///   <item><see cref="Schedule"/> 連続呼び出しは「最後の 1 回」のみが debounce 経過後に保存。</item>
///   <item>実行中の <c>saveAction</c> がある間に <see cref="Schedule"/> / <see cref="FlushNowAsync"/>
///     が来ても、 gate が解放されるまで次の保存は始まらない (EF DbContext race 防止)。</item>
///   <item><see cref="FlushNowAsync"/> は保留中タイマーを cancel し、 即座に保存を実行 + その完了を await。</item>
/// </list>
/// <para><b>Dispose 仕様</b>: CTS cancel + <c>_disposed</c> フラグだけ立てる。 内部の
/// <see cref="SemaphoreSlim"/> は GC 任せ (進行中の <c>saveAction</c> が gate に触る可能性があるため)。
/// 進行中の保存は途中で cancel しない (DB 半端書き込み防止)。 終了時の確実な flush は呼び出し側で
/// <see cref="FlushNowAsync"/> を await してから Dispose する想定。</para>
/// </remarks>
public sealed class AutoSaveDispatcher : IDisposable
{
    private readonly TimeSpan _debounce;
    private readonly Func<CancellationToken, Task> _saveAction;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public AutoSaveDispatcher(TimeSpan debounce, Func<CancellationToken, Task> saveAction)
    {
        ArgumentNullException.ThrowIfNull(saveAction);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        _debounce = debounce;
        _saveAction = saveAction;
    }

    /// <summary>
    /// debounce 経過後に <c>saveAction</c> を 1 回だけ実行するよう予約する。 既存の予約は
    /// cancel されて新タイマーで上書きされる。 Dispose 後の呼び出しは静かに無視される。
    /// </summary>
    public void Schedule()
    {
        if (_disposed) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localToken = _cts.Token;

        // 例外で UnobservedTaskException にならないよう、 内部で try/catch して握る。
        _ = RunAfterDelayAsync(localToken);
    }

    private async Task RunAfterDelayAsync(CancellationToken ct)
    {
        // ConfigureAwait(false) は付けない。 呼び出し元 (Schedule) の SynchronizationContext を
        // 維持して、 Avalonia UI thread から Schedule された場合は continuation も UI thread で resume させる。
        // saveAction が EF Core DbContext を触る経路で、 UI thread と TP thread から同時に DbContext を
        // 触ると 「A second operation was started on this context」 例外が発生して auto-save が静かに失敗する。
        try
        {
            await Task.Delay(_debounce, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ExecuteSaveSerializedAsync(ct);
    }

    private async Task ExecuteSaveSerializedAsync(CancellationToken ct)
    {
        // gate を取って saveAction を直列化。 Dispose 後に gate.Wait を呼ぶと
        // ObjectDisposedException が出るので、 _disposed チェックを先に行う。
        if (_disposed) return;
        try
        {
            // 上記同様 ConfigureAwait(false) は付けない (UI thread で resume させる)。
            await _gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            // Dispose と並走したケース。 静かに抜ける。
            return;
        }

        try
        {
            // 進行中 cancel は許容しない仕様。 ct が cancel 済みでも保存は完遂させる
            // (DB 半端書き込み防止)。 Save 内部で長時間ブロックする想定もないので問題なし。
            // ConfigureAwait(false) は付けない (UI thread を維持)。
            await _saveAction(CancellationToken.None);
        }
        catch
        {
            // saveAction 内で例外を握っていない場合の最終ガード。
            // VM 側で StatusMessage に反映する想定なので、 ここでは握る。
        }
        finally
        {
            try { _gate.Release(); }
            catch (ObjectDisposedException) { /* Dispose 後の release は無視 */ }
            catch (SemaphoreFullException) { /* 念のため */ }
        }
    }

    /// <summary>
    /// 保留中のタイマーを cancel し、 即座に <c>saveAction</c> を実行 + その完了を待つ。
    /// 既に <c>saveAction</c> が進行中なら、 その完了を待ってから新しい saveAction を 1 回実行する。
    /// 保留も進行中もなければ即座に return (no-op)。
    /// ConfigureAwait(false) は付けない (UI thread を維持して EF DbContext race を防ぐ)。
    /// </summary>
    public async Task FlushNowAsync(CancellationToken ct = default)
    {
        if (_disposed) return;

        // 保留中タイマーがあれば cancel して、 こちらで即実行に切り替える。
        var hadPending = _cts is { IsCancellationRequested: false };
        _cts?.Cancel();

        // 保留も進行中もなければ何もしない (SemaphoreSlim を取りに行くと進行中タスクと競合する可能性がある)。
        // ただし「進行中のみで保留なし」のケースでは gate を取って完了待ちする必要がある。
        // 単純化のため: 「保留があった or 進行中の可能性」がある限り gate を一度取りに行く。
        // gate を取れた = 進行中タスクが終わった、 という同期点として使える。
        if (!hadPending)
        {
            // 進行中の確認 (gate が空いているか) も含めて、 gate を取って即 release。
            try
            {
                await _gate.WaitAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            try { _gate.Release(); }
            catch (ObjectDisposedException) { }
            catch (SemaphoreFullException) { }
            return;
        }

        // hadPending: 保留中の Schedule を奪って即実行。 gate で進行中とも直列化される。
        await ExecuteSaveSerializedAsync(ct);
    }

    /// <summary>保留中のタイマーを cancel する。 保存は実行しない。</summary>
    public void Cancel()
    {
        if (_disposed) return;
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        // _gate は Dispose しない (進行中 saveAction が触る可能性、 ObjectDisposedException 防止)。
        // SemaphoreSlim 1 個分のメモリは GC + プロセス終了で自然解放される。
    }
}
