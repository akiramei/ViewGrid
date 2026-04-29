using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// グリッドの列・行ロック配列を更新する操作（ヘッダの 🔒 トグル）の Undo/Redo ラッパ。
/// before/after のロック配列を保持して両方向に <see cref="UpdateGridLocksUseCase"/> を呼ぶ。
/// </summary>
public sealed class UpdateGridLocksCommand : IUndoableCommand
{
    private readonly UpdateGridLocksUseCase _useCase;
    private readonly Guid _gridId;
    private readonly ImmutableArray<bool> _beforeCol;
    private readonly ImmutableArray<bool> _beforeRow;
    private readonly ImmutableArray<bool> _afterCol;
    private readonly ImmutableArray<bool> _afterRow;
    private readonly string _description;

    public UpdateGridLocksCommand(
        UpdateGridLocksUseCase useCase,
        Guid gridId,
        ImmutableArray<bool> beforeCol,
        ImmutableArray<bool> beforeRow,
        ImmutableArray<bool> afterCol,
        ImmutableArray<bool> afterRow,
        string description)
    {
        _useCase = useCase;
        _gridId = gridId;
        _beforeCol = beforeCol;
        _beforeRow = beforeRow;
        _afterCol = afterCol;
        _afterRow = afterRow;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(
            _gridId,
            (IReadOnlyList<bool>)_afterCol,
            (IReadOnlyList<bool>)_afterRow,
            ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(
            _gridId,
            (IReadOnlyList<bool>)_beforeCol,
            (IReadOnlyList<bool>)_beforeRow,
            ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
