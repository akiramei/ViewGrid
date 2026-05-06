using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Interfaces;

public interface IGridCanvasRepository
{
    Task<IReadOnlyList<GridCanvas>> FindAllAsync(CancellationToken ct = default);
    Task<GridCanvas?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<ErrorOr<GridCanvas>> AddAsync(GridCanvas grid, CancellationToken ct = default);
    Task<ErrorOr<Success>> UpdateAsync(GridCanvas grid, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default);
}
