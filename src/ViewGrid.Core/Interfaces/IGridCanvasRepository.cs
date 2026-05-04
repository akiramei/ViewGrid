using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Interfaces;

public interface IGridCanvasRepository
{
    Task<IReadOnlyList<GridCanvas>> FindAllAsync(CancellationToken ct = default);
    Task<GridCanvas?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<GridCanvas?> FindActiveAsync(CancellationToken ct = default);
    Task<ErrorOr<GridCanvas>> AddAsync(GridCanvas grid, CancellationToken ct = default);
    Task<ErrorOr<Success>> UpdateAsync(GridCanvas grid, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 指定 ID をアクティブにし、他のグリッドは非アクティブにする（排他制御）。
    /// </summary>
    Task<ErrorOr<Success>> SetActiveAsync(Guid id, CancellationToken ct = default);
}
