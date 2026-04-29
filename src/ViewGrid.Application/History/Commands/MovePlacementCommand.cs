using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 配置を別セルへ移動する操作の Undo/Redo ラッパ。
/// 同一の <see cref="MovePlacementUseCase"/> を Execute（before→after）と
/// Undo（after→before）で対称に呼ぶだけで成立する。
/// </summary>
public sealed class MovePlacementCommand : IUndoableCommand
{
    private readonly MovePlacementUseCase _move;
    private readonly Guid _gridId;
    private readonly Guid _placementId;
    private readonly CellPosition _before;
    private readonly CellPosition _after;
    private readonly string _description;

    public MovePlacementCommand(
        MovePlacementUseCase moveUseCase,
        Guid gridId,
        Guid placementId,
        CellPosition before,
        CellPosition after,
        string description)
    {
        _move = moveUseCase;
        _gridId = gridId;
        _placementId = placementId;
        _before = before;
        _after = after;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _move.ExecuteAsync(_placementId, _after, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _move.ExecuteAsync(_placementId, _before, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
