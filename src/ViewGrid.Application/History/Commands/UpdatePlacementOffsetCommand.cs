using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 配置の <see cref="ViewGrid.Core.Entities.GridPlacement.PixelOffsetX"/> /
/// <c>PixelOffsetY</c> を更新する操作の Undo/Redo ラッパ。
/// before/after の値を保持して対称に <see cref="UpdatePlacementOffsetUseCase"/> を呼ぶ。
/// </summary>
public sealed class UpdatePlacementOffsetCommand : IUndoableCommand
{
    private readonly UpdatePlacementOffsetUseCase _useCase;
    private readonly Guid _gridId;
    private readonly Guid _placementId;
    private readonly int _beforeX;
    private readonly int _beforeY;
    private readonly int _afterX;
    private readonly int _afterY;
    private readonly string _description;

    public UpdatePlacementOffsetCommand(
        UpdatePlacementOffsetUseCase useCase,
        Guid gridId,
        Guid placementId,
        int beforeX,
        int beforeY,
        int afterX,
        int afterY,
        string description)
    {
        _useCase = useCase;
        _gridId = gridId;
        _placementId = placementId;
        _beforeX = beforeX;
        _beforeY = beforeY;
        _afterX = afterX;
        _afterY = afterY;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
        => _useCase.ExecuteAsync(_placementId, _afterX, _afterY, ct);

    public Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
        => _useCase.ExecuteAsync(_placementId, _beforeX, _beforeY, ct);
}
