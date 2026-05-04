using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Interfaces;

public interface IGridPlacementRepository
{
    Task<IReadOnlyList<GridPlacement>> FindByGridIdAsync(Guid gridId, CancellationToken ct = default);

    /// <summary>
    /// 指定 ImageCopy を参照する全 Placement を返す。共有特性（OccupySize 等）の変更時に
    /// 全グリッドにまたがる影響範囲を確認するために使う。
    /// </summary>
    Task<IReadOnlyList<GridPlacement>> FindByCopyIdAsync(Guid copyId, CancellationToken ct = default);

    Task<GridPlacement?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<ErrorOr<GridPlacement>> AddAsync(GridPlacement placement, CancellationToken ct = default);
    Task<ErrorOr<Success>> UpdateAsync(GridPlacement placement, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteByGridIdAsync(Guid gridId, CancellationToken ct = default);
}
