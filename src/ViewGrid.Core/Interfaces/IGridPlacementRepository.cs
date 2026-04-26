using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Interfaces;

public interface IGridPlacementRepository
{
    Task<IReadOnlyList<GridPlacement>> FindByGridIdAsync(Guid gridId, CancellationToken ct = default);
    Task<GridPlacement?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<ErrorOr<GridPlacement>> AddAsync(GridPlacement placement, CancellationToken ct = default);
    Task<ErrorOr<Success>> UpdateAsync(GridPlacement placement, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteByGridIdAsync(Guid gridId, CancellationToken ct = default);
}
