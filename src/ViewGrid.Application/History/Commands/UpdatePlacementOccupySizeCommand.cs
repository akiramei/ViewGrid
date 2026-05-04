using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 配置の <see cref="GridPlacement.OccupySize"/> を更新する操作の Undo/Redo ラッパ。
/// before/after の値を保持して対称に <see cref="UpdatePlacementOccupySizeUseCase"/> を呼ぶ。
/// </summary>
public sealed class UpdatePlacementOccupySizeCommand : IUndoableCommand
{
    private readonly UpdatePlacementOccupySizeUseCase _useCase;
    private readonly Guid _gridId;
    private readonly Guid _placementId;
    private readonly OccupySize _before;
    private readonly OccupySize _after;
    private readonly string _description;

    public UpdatePlacementOccupySizeCommand(
        UpdatePlacementOccupySizeUseCase useCase,
        Guid gridId,
        Guid placementId,
        OccupySize before,
        OccupySize after,
        string description)
    {
        _useCase = useCase;
        _gridId = gridId;
        _placementId = placementId;
        _before = before;
        _after = after;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
        => _useCase.ExecuteAsync(_placementId, _after, ct);

    public Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
        => _useCase.ExecuteAsync(_placementId, _before, ct);
}
