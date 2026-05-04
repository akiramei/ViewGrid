using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// アクティブグリッド切替操作の Undo/Redo ラッパ。before の active grid id（Execute 直前の
/// アクティブだったグリッド）を保持して、Undo で元に戻す。
/// アクティブが存在しなかった場合（<c>beforeActiveId == null</c>）は Undo しても
/// 何もしない（=ViewGrid では起動時に必ず 1 つアクティブ化される設計のため、通常は到達しない）。
/// </summary>
public sealed class SetActiveGridCanvasCommand : IUndoableCommand
{
    private readonly SetActiveGridCanvasUseCase _useCase;
    private readonly Guid _afterActiveId;
    private readonly Guid? _beforeActiveId;
    private readonly string _description;

    public SetActiveGridCanvasCommand(
        SetActiveGridCanvasUseCase useCase,
        Guid afterActiveId,
        Guid? beforeActiveId,
        string description)
    {
        _useCase = useCase;
        _afterActiveId = afterActiveId;
        _beforeActiveId = beforeActiveId;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _afterActiveId;

    public Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
        => _useCase.ExecuteAsync(_afterActiveId, ct);

    public Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        if (_beforeActiveId is null)
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        return _useCase.ExecuteAsync(_beforeActiveId.Value, ct);
    }
}
