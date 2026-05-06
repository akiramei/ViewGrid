using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// グリッドの CanvasSize 変更操作の Undo/Redo ラッパ。 before/after の <see cref="PixelSize"/>
/// を保持して <see cref="UpdateGridCanvasSizeUseCase"/> を両方向に呼ぶ。
/// </summary>
public sealed class UpdateGridCanvasSizeCommand : IUndoableCommand
{
    private readonly UpdateGridCanvasSizeUseCase _useCase;
    private readonly Guid _gridId;
    private readonly PixelSize _before;
    private readonly PixelSize _after;
    private readonly string _description;

    public UpdateGridCanvasSizeCommand(
        UpdateGridCanvasSizeUseCase useCase,
        Guid gridId,
        PixelSize before,
        PixelSize after,
        string description)
    {
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
        var result = await _useCase.ExecuteAsync(_gridId, _after.Width, _after.Height, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_gridId, _before.Width, _before.Height, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
