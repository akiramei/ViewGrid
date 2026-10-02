namespace ViewGrid.Application.Preview;

/// <summary>
/// 開きっぱなしのプレビューを、 保存済みの内容の変化に合わせて作り直す小さなコーディネータ。
/// <see cref="Request"/> を何度呼んでも、 静止して <c>debounce</c> が経過してから最新の 1 回だけ描画する
/// (スライダー操作のような連続変化を 1 回にまとめる)。 新しい要求が来たら、 待機中・描画中の古い要求は
/// 取り消され、 結果は捨てられる。 描画は同時に 1 本しか走らせない。
/// </summary>
/// <remarks>
/// 描画結果はスレッドプール上の継続から <c>onRendered</c> へ渡される。 UI へ反映する側がスレッドを切り替える。
/// 破棄 (<see cref="Dispose"/>) 後は、 実行中だった描画も含めて <c>onRendered</c> を呼ばない。
/// </remarks>
public sealed class LivePreviewRefresher : IDisposable
{
    private readonly TimeSpan _debounce;
    private readonly Func<CancellationToken, Task<byte[]?>> _render;
    private readonly Action<byte[]?> _onRendered;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    /// <param name="debounce">最後の <see cref="Request"/> から描画を始めるまでの静止時間。</param>
    /// <param name="render">PNG を描画する。 表示できる内容がない / 失敗したときは <c>null</c>。</param>
    /// <param name="onRendered">描画結果 (または <c>null</c>) の通知先。 最新の要求の結果だけが届く。</param>
    public LivePreviewRefresher(
        TimeSpan debounce,
        Func<CancellationToken, Task<byte[]?>> render,
        Action<byte[]?> onRendered)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(onRendered);
        _debounce = debounce;
        _render = render;
        _onRendered = onRendered;
    }

    /// <summary>プレビューの再描画を要求する。 古い未完了の要求は取り消される。 スレッドセーフ。</summary>
    public void Request()
    {
        CancellationToken ct;
        lock (_sync)
        {
            if (_disposed) return;
            _current?.Cancel();
            _current = new CancellationTokenSource();
            ct = _current.Token;
        }
        _ = RunAsync(ct);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        byte[]? bytes;
        try
        {
            await Task.Delay(_debounce, ct).ConfigureAwait(false);
            await _renderGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                bytes = await _render(ct).ConfigureAwait(false);
            }
            finally
            {
                _renderGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
#pragma warning disable CA1031 // 描画の失敗でプロセスを落とさない (null として通知する)
        catch (Exception)
#pragma warning restore CA1031
        {
            bytes = null;
        }

        // 取り消された (= 新しい要求が出た / 破棄された) 結果は古いので捨てる。
        if (ct.IsCancellationRequested) return;
        _onRendered(bytes);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _current?.Cancel();
        }
    }
}
