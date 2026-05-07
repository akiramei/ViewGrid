namespace ViewGrid.Application.AutoSave;

/// <summary>
/// VM 側に分散していた auto-save の bookkeeping (設定 ON/OFF gate / dirty 判定 /
/// signature-dedup / 失敗 retry guard) を集約するヘルパ。 内部に
/// <see cref="AutoSaveDispatcher"/> を 1 個保持し、 debounce + serialization は
/// そちらに委譲する。
/// </summary>
/// <remarks>
/// VM の責務は <see cref="NotifyEdited"/> / <see cref="FlushAsync"/> / <see cref="Cancel"/> /
/// <see cref="ResetFailureGuard"/> を呼ぶだけ。 各 VM は次のいずれかのタイミングで
/// 必要な API を呼び出す:
/// <list type="bullet">
///   <item>編集系プロパティ変更時 → <see cref="NotifyEdited"/></item>
///   <item>編集対象 (placement / grid item 等) 切替時 → <see cref="ResetFailureGuard"/></item>
///   <item>切替・終了等で確実に保存したい時 → <see cref="FlushAsync"/></item>
///   <item>設定 OFF 化等で保留分を捨てたい時 → <see cref="Cancel"/></item>
/// </list>
/// </remarks>
public sealed class SaveCoordinator : IDisposable
{
    private readonly AutoSaveDispatcher _dispatcher;
    private readonly Func<bool> _isEnabled;
    private readonly Func<bool> _isDirty;
    private readonly Func<string> _signatureProvider;
    private readonly Func<CancellationToken, Task<bool>> _saveAction;
    private string? _lastFailedSignature;
    private bool _disposed;

    /// <param name="debounce">編集静止後 saveAction を実行するまでの待ち時間。</param>
    /// <param name="isEnabled">auto-save が ON か (設定値の polling delegate)。</param>
    /// <param name="isDirty">対象 VM が現在 dirty か。 false なら Schedule 不要。</param>
    /// <param name="signatureProvider">「同じ編集状態」 を識別する文字列を返す delegate。
    /// 失敗 retry guard と「最新値で再 Schedule」 判定に使う。</param>
    /// <param name="saveAction">実際の保存処理。 成功で <c>true</c>、 失敗で <c>false</c> を返す。
    /// 失敗時は同 signature での再 Schedule を抑止する。</param>
    public SaveCoordinator(
        TimeSpan debounce,
        Func<bool> isEnabled,
        Func<bool> isDirty,
        Func<string> signatureProvider,
        Func<CancellationToken, Task<bool>> saveAction)
    {
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentNullException.ThrowIfNull(isDirty);
        ArgumentNullException.ThrowIfNull(signatureProvider);
        ArgumentNullException.ThrowIfNull(saveAction);
        _isEnabled = isEnabled;
        _isDirty = isDirty;
        _signatureProvider = signatureProvider;
        _saveAction = saveAction;
        _dispatcher = new AutoSaveDispatcher(debounce, RunSaveAsync);
    }

    /// <summary>
    /// 編集系プロパティ変更時に呼ぶ。 設定 OFF / dirty なし / 同 signature 失敗中はスキップする。
    /// 通過したら debounce 経過後に saveAction が 1 回だけ走る。 連続呼び出しは debounce で
    /// 最後の 1 回だけにまとまる (内部 dispatcher の挙動)。
    /// </summary>
    public void NotifyEdited()
    {
        if (_disposed) return;
        if (!_isEnabled()) return;
        if (!_isDirty()) return;
        var sig = _signatureProvider();
        if (sig == _lastFailedSignature) return;
        _dispatcher.Schedule();
    }

    /// <summary>
    /// 保留中の auto-save を即実行 + その完了を await する (進行中の場合は完了待機)。
    /// 編集対象切替・アプリ終了・ワークスペース切替等で「取りこぼしを許さない」 経路に置く。
    /// </summary>
    public Task FlushAsync(CancellationToken ct = default) => _dispatcher.FlushNowAsync(ct);

    /// <summary>
    /// 保留中タイマーを cancel する (進行中の保存は完遂)。 設定が OFF にされた瞬間に呼ぶ。
    /// </summary>
    public void Cancel()
    {
        if (_disposed) return;
        _dispatcher.Cancel();
    }

    /// <summary>
    /// 失敗 signature をクリアして同値での再試行を許可する。 通常は signature に編集対象 ID
    /// を含めるため自動的に別物として扱われるが、 編集対象切替時に明示的に呼ぶことで
    /// 「signature が偶然衝突した」 という稀なケースでも retry が走るようにする。
    /// </summary>
    public void ResetFailureGuard() => _lastFailedSignature = null;

    private async Task RunSaveAsync(CancellationToken ct)
    {
        // 実行直前の signature を捕捉。 saveAction 中に値が変わっても、 失敗 guard は
        // 「実行を開始した時点の値」 を覚える (実行後に同値で再来したら抑止)。
        var sigBefore = _signatureProvider();
        var ok = await _saveAction(ct).ConfigureAwait(false);
        _lastFailedSignature = ok ? null : sigBefore;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispatcher.Dispose();
    }
}
