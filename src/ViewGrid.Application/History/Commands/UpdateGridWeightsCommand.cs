using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// グリッドの列・行の重み配列を更新する操作（境界ハンドルドラッグの確定 / フィット）の
/// Undo/Redo ラッパ。before/after の重み配列を保持して両方向に
/// <see cref="UpdateGridWeightsUseCase"/> を呼ぶ。null は「変更なし」だが Undo/Redo では
/// 必ず明示的な配列を渡す（呼び出し側で取得した snapshot を使う）。
/// </summary>
public sealed class UpdateGridWeightsCommand : IUndoableCommand
{
    private readonly UpdateGridWeightsUseCase _useCase;
    private readonly Guid _gridId;
    private readonly ImmutableArray<int> _beforeCol;
    private readonly ImmutableArray<int> _beforeRow;
    private readonly ImmutableArray<int> _afterCol;
    private readonly ImmutableArray<int> _afterRow;
    private readonly string _description;

    public UpdateGridWeightsCommand(
        UpdateGridWeightsUseCase useCase,
        Guid gridId,
        ImmutableArray<int> beforeCol,
        ImmutableArray<int> beforeRow,
        ImmutableArray<int> afterCol,
        ImmutableArray<int> afterRow,
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
            (IReadOnlyList<int>)_afterCol,
            (IReadOnlyList<int>)_afterRow,
            ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(
            _gridId,
            (IReadOnlyList<int>)_beforeCol,
            (IReadOnlyList<int>)_beforeRow,
            ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
