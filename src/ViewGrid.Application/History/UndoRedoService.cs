using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;

namespace ViewGrid.Application.History;

/// <summary>
/// <see cref="IUndoRedoService"/> の標準実装。最大 <see cref="MaxStackSize"/> エントリの
/// LinkedList ベース Undo スタックと Stack ベース Redo スタックを保持する。
/// 同時実行は <see cref="SemaphoreSlim"/> で直列化し、ExecuteAsync / UndoAsync / RedoAsync の
/// レース起因二重 push / 二重 pop を防ぐ。
/// </summary>
public sealed class UndoRedoService : IUndoRedoService, IDisposable
{
    /// <summary>Undo スタックの上限。設計書（technical-specification.md §3.6.2）に準拠。</summary>
    public const int MaxStackSize = 50;

    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public bool CanUndo
    {
        get
        {
            lock (_undo)
                return _undo.Count > 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_undo)
                return _redo.Count > 0;
        }
    }

    public string? NextUndoDescription
    {
        get
        {
            lock (_undo)
                return _undo.Last?.Value.Description;
        }
    }

    public string? NextRedoDescription
    {
        get
        {
            lock (_undo)
                return _redo.Count > 0 ? _redo.Peek().Description : null;
        }
    }

    public event Action? StateChanged;

    public async Task<ErrorOr<Success>> ExecuteAsync(IUndoableCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await command.ExecuteAsync(ct).ConfigureAwait(false);
            if (result.IsError)
                return result;

            lock (_undo)
            {
                _undo.AddLast(command);
                while (_undo.Count > MaxStackSize)
                    _undo.RemoveFirst();
                _redo.Clear();
            }
            RaiseStateChanged();
            return Result.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IUndoableCommand? command;
            lock (_undo)
            {
                if (_undo.Last is null)
                    return Result.Success;
                command = _undo.Last.Value;
                _undo.RemoveLast();
            }

            var result = await command.UndoAsync(ct).ConfigureAwait(false);
            if (result.IsError)
            {
                // 依存破綻（NotFound 等）。スタックを破棄して整合を保つ。
                lock (_undo)
                {
                    _undo.Clear();
                    _redo.Clear();
                }
                RaiseStateChanged();
                return result;
            }

            lock (_undo)
                _redo.Push(command);
            RaiseStateChanged();
            return Result.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ErrorOr<Success>> RedoAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IUndoableCommand? command;
            lock (_undo)
            {
                if (_redo.Count == 0)
                    return Result.Success;
                command = _redo.Pop();
            }

            var result = await command.ExecuteAsync(ct).ConfigureAwait(false);
            if (result.IsError)
            {
                // 依存破綻。スタックを破棄。
                lock (_undo)
                {
                    _undo.Clear();
                    _redo.Clear();
                }
                RaiseStateChanged();
                return result;
            }

            lock (_undo)
            {
                _undo.AddLast(command);
                while (_undo.Count > MaxStackSize)
                    _undo.RemoveFirst();
            }
            RaiseStateChanged();
            return Result.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Clear()
    {
        bool changed;
        lock (_undo)
        {
            changed = _undo.Count > 0 || _redo.Count > 0;
            _undo.Clear();
            _redo.Clear();
        }
        if (changed)
            RaiseStateChanged();
    }

    public IReadOnlyList<HistoryEntry> History
    {
        get
        {
            lock (_undo)
            {
                var n = _undo.Count + _redo.Count;
                var current = _undo.Count - 1;
                var result = new List<HistoryEntry>(n);
                var i = 0;
                foreach (var c in _undo)
                {
                    result.Add(new HistoryEntry(c.Description, i, IsApplied: true, IsCurrent: i == current));
                    i++;
                }
                // _redo は top-first 列挙（次に Redo されるエントリが先頭）。
                // これは時系列上「最近 Undo されたもの」から並ぶので、適用済みエントリの直後に置けば
                // 「最古→最新」の連続した時系列ビューになる。
                foreach (var c in _redo)
                {
                    result.Add(new HistoryEntry(c.Description, i, IsApplied: false, IsCurrent: false));
                    i++;
                }
                return result;
            }
        }
    }

    public int CurrentIndex
    {
        get { lock (_undo) return _undo.Count - 1; }
    }

    public async Task<ErrorOr<Success>> JumpToAsync(int targetIndex, CancellationToken ct = default)
    {
        int totalCount;
        lock (_undo) totalCount = _undo.Count + _redo.Count;
        if (targetIndex < -1 || targetIndex >= totalCount)
            return Error.Validation(
                "History.OutOfRange",
                $"指定された Index ({targetIndex}) は履歴範囲 [-1, {totalCount - 1}] 外です。");

        // 前進（Redo）または後退（Undo）を 1 ステップずつ実行。
        // CurrentIndex は UndoAsync/RedoAsync が成功するたびに ±1 動くので、
        // ループの停止条件（targetIndex に到達 / 失敗で early return）は自然に成立する。
        while (CurrentIndex < targetIndex)
        {
            var r = await RedoAsync(ct).ConfigureAwait(false);
            if (r.IsError) return r;
        }
        while (CurrentIndex > targetIndex)
        {
            var r = await UndoAsync(ct).ConfigureAwait(false);
            if (r.IsError) return r;
        }
        return Result.Success;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }
}
