using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// グリッド名変更操作の Undo/Redo ラッパ。before/after の名前を保持して
/// <see cref="RenameGridCanvasUseCase"/> を両方向に呼ぶ。
/// </summary>
public sealed class RenameGridCanvasCommand : IUndoableCommand
{
    private readonly RenameGridCanvasUseCase _useCase;
    private readonly Guid _gridId;
    private readonly string _before;
    private readonly string _after;
    private readonly string _description;

    public RenameGridCanvasCommand(
        RenameGridCanvasUseCase useCase,
        Guid gridId,
        string before,
        string after,
        string description)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        _useCase = useCase;
        _gridId = gridId;
        _before = before;
        _after = after;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_gridId, _after, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_gridId, _before, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
