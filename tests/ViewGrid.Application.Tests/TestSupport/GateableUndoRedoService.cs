using ErrorOr;
using ViewGrid.Application.History;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// 実際の <see cref="UndoRedoService"/> へ委譲しつつ、 新規コマンドの実行 (= 保存) をテストが止めたり
/// 再開したりできる履歴サービス。 「保存の最中に別の編集・操作が起きる」 順序を決定的に作るために使う。
/// </summary>
internal sealed class GateableUndoRedoService : IUndoRedoService, IDisposable
{
    private readonly UndoRedoService _inner = new();
    private TaskCompletionSource? _gate;
    private TaskCompletionSource? _entered;

    /// <summary>保存 (ExecuteAsync) を、 <see cref="Release"/> が呼ばれるまで止める。</summary>
    public void Block()
    {
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>止めた保存が実際に ExecuteAsync へ入る (= 保存の最中になる) まで待つ。</summary>
    public Task WaitUntilBlockedAsync() => (_entered ?? throw new InvalidOperationException("Block() 未実行")).Task
        .WaitAsync(TimeSpan.FromSeconds(5));

    /// <summary>止めていた保存を再開する。</summary>
    public void Release() => _gate?.TrySetResult();

    public async Task<ErrorOr<Success>> ExecuteAsync(IUndoableCommand command, CancellationToken ct = default)
    {
        var gate = _gate;
        if (gate is not null)
        {
            _entered?.TrySetResult();
            await gate.Task.ConfigureAwait(false);
        }
        return await _inner.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public bool CanUndo => _inner.CanUndo;
    public bool CanRedo => _inner.CanRedo;
    public string? NextUndoDescription => _inner.NextUndoDescription;
    public string? NextRedoDescription => _inner.NextRedoDescription;
    public IReadOnlyList<HistoryEntry> History => _inner.History;
    public int CurrentIndex => _inner.CurrentIndex;

    public event Action? StateChanged
    {
        add => _inner.StateChanged += value;
        remove => _inner.StateChanged -= value;
    }

    public event Action<IUndoableCommand>? Undone
    {
        add => _inner.Undone += value;
        remove => _inner.Undone -= value;
    }

    public event Action<IUndoableCommand>? Redone
    {
        add => _inner.Redone += value;
        remove => _inner.Redone -= value;
    }

    public Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default) => _inner.UndoAsync(ct);
    public Task<ErrorOr<Success>> RedoAsync(CancellationToken ct = default) => _inner.RedoAsync(ct);
    public Task<ErrorOr<Success>> JumpToAsync(int targetIndex, CancellationToken ct = default) => _inner.JumpToAsync(targetIndex, ct);
    public void Clear() => _inner.Clear();
    public void Dispose() => _inner.Dispose();
}
