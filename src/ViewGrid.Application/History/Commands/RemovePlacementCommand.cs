using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 既存配置を削除する操作の Undo/Redo ラッパ。
/// 構築時に削除前 snapshot を渡してもらい、Undo では <see cref="IGridPlacementRepository.AddAsync"/>
/// で同じ Id（PixelOffset / PlacementOrder 等も含めた完全な状態）で再 INSERT する。
/// </summary>
public sealed class RemovePlacementCommand : IUndoableCommand
{
    private readonly RemovePlacementUseCase _remove;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly GridPlacement _snapshot;
    private readonly string _description;

    public RemovePlacementCommand(
        RemovePlacementUseCase removeUseCase,
        IGridPlacementRepository placementRepository,
        GridPlacement snapshot,
        string description)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _remove = removeUseCase;
        _placementRepository = placementRepository;
        _snapshot = snapshot;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _snapshot.GridId;

    public Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
        => _remove.ExecuteAsync(_snapshot.Id, ct);

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var add = await _placementRepository.AddAsync(_snapshot, ct).ConfigureAwait(false);
        return add.IsError ? add.Errors : Result.Success;
    }
}
