using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 2 つの配置を入れ替える操作の Undo/Redo ラッパ。
/// <see cref="SwapPlacementsUseCase"/> は対称（同じ引数で 2 回呼ぶと元に戻る）なので、
/// Execute と Undo は同じ呼び出しでよい。
/// </summary>
public sealed class SwapPlacementsCommand : IUndoableCommand
{
    private readonly SwapPlacementsUseCase _swap;
    private readonly Guid _gridId;
    private readonly Guid _aId;
    private readonly Guid _bId;
    private readonly string _description;

    public SwapPlacementsCommand(
        SwapPlacementsUseCase swapUseCase,
        Guid gridId,
        Guid aId,
        Guid bId,
        string description)
    {
        _swap = swapUseCase;
        _gridId = gridId;
        _aId = aId;
        _bId = bId;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
        => _swap.ExecuteAsync(_aId, _bId, ct);

    public Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
        => _swap.ExecuteAsync(_aId, _bId, ct);
}
