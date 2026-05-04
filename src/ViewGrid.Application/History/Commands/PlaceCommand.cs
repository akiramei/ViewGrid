using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 論理コピーをグリッドに新規配置する操作の Undo/Redo ラッパ。
/// 初回 Execute は <see cref="PlaceImageCopyUseCase"/> を呼んで検証付きで配置し、
/// 結果の <see cref="GridPlacement"/> をスナップショットとして保持する。Redo は
/// 同じ Id で <see cref="IGridPlacementRepository.AddAsync"/> を呼んで再 INSERT する
/// （Undo→Redo 間に他の操作は走らない設計のため検証は不要）。Undo は
/// <see cref="RemovePlacementUseCase"/> で物理削除する。
/// </summary>
public sealed class PlaceCommand : IUndoableCommand
{
    private readonly PlaceImageCopyUseCase _place;
    private readonly RemovePlacementUseCase _remove;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly Guid _gridId;
    private readonly Guid _copyId;
    private readonly CellPosition _position;
    private readonly string _description;
    private GridPlacement? _snapshot;
    private bool _firstExecute = true;

    public PlaceCommand(
        PlaceImageCopyUseCase placeUseCase,
        RemovePlacementUseCase removeUseCase,
        IGridPlacementRepository placementRepository,
        Guid gridId,
        Guid copyId,
        CellPosition position,
        string description)
    {
        _place = placeUseCase;
        _remove = removeUseCase;
        _placementRepository = placementRepository;
        _gridId = gridId;
        _copyId = copyId;
        _position = position;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    /// <summary>初回 Execute 後に確定する placement の Id。VM 側の選択復元等で利用する。</summary>
    public Guid? CreatedPlacementId => _snapshot?.Id;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        if (_firstExecute)
        {
            var result = await _place.ExecuteAsync(_gridId, _copyId, _position, ct).ConfigureAwait(false);
            if (result.IsError)
                return result.Errors;
            _snapshot = result.Value;
            _firstExecute = false;
            return Result.Success;
        }

        // Redo: 同じ Id で再 INSERT。Undo→Redo 間に他操作は入らないため検証不要。
        var add = await _placementRepository.AddAsync(_snapshot!, ct).ConfigureAwait(false);
        return add.IsError ? add.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        if (_snapshot is null)
            return Result.Success;
        return await _remove.ExecuteAsync(_snapshot.Id, ct).ConfigureAwait(false);
    }
}
