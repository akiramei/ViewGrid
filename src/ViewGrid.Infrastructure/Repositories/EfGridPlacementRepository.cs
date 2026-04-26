using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Infrastructure.Persistence;

namespace ViewGrid.Infrastructure.Repositories;

internal sealed class EfGridPlacementRepository(ViewGridDbContext db) : IGridPlacementRepository
{
    public async Task<IReadOnlyList<GridPlacement>> FindByGridIdAsync(Guid gridId, CancellationToken ct = default) =>
        await db.GridPlacements
            .AsNoTracking()
            .Where(x => x.GridId == gridId)
            .OrderBy(x => x.PlacementOrder)
            .ToListAsync(ct);

    public async Task<GridPlacement?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.GridPlacements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<ErrorOr<GridPlacement>> AddAsync(GridPlacement placement, CancellationToken ct = default)
    {
        db.GridPlacements.Add(placement);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(placement).State = EntityState.Detached;
        }
        return placement;
    }

    public async Task<ErrorOr<Success>> UpdateAsync(GridPlacement placement, CancellationToken ct = default)
    {
        var existing = await db.GridPlacements.FirstOrDefaultAsync(x => x.Id == placement.Id, ct);
        if (existing is null)
            return Error.NotFound("GridPlacement.NotFound", $"GridPlacement {placement.Id} が見つかりません。");

        db.Entry(existing).CurrentValues.SetValues(placement);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(existing).State = EntityState.Detached;
        }
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await db.GridPlacements.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0
            ? Result.Success
            : Error.NotFound("GridPlacement.NotFound", $"GridPlacement {id} が見つかりません。");
    }

    public async Task<ErrorOr<Success>> DeleteByGridIdAsync(Guid gridId, CancellationToken ct = default)
    {
        await db.GridPlacements.Where(x => x.GridId == gridId).ExecuteDeleteAsync(ct);
        return Result.Success;
    }
}
