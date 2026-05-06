using ErrorOr;
using Microsoft.EntityFrameworkCore;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Infrastructure.Persistence;

namespace ViewGrid.Infrastructure.Repositories;

internal sealed class EfGridCanvasRepository(ViewGridDbContext db) : IGridCanvasRepository
{
    public async Task<IReadOnlyList<GridCanvas>> FindAllAsync(CancellationToken ct = default) =>
        await db.GridCanvases.AsNoTracking().OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);

    public async Task<GridCanvas?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.GridCanvases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<ErrorOr<GridCanvas>> AddAsync(GridCanvas grid, CancellationToken ct = default)
    {
        db.GridCanvases.Add(grid);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(grid).State = EntityState.Detached;
        }
        return grid;
    }

    public async Task<ErrorOr<Success>> UpdateAsync(GridCanvas grid, CancellationToken ct = default)
    {
        var existing = await db.GridCanvases.FirstOrDefaultAsync(x => x.Id == grid.Id, ct);
        if (existing is null)
            return Error.NotFound("GridCanvas.NotFound", $"GridCanvas {grid.Id} が見つかりません。");

        db.Entry(existing).CurrentValues.SetValues(grid);
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
        var rows = await db.GridCanvases.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0
            ? Result.Success
            : Error.NotFound("GridCanvas.NotFound", $"GridCanvas {id} が見つかりません。");
    }
}
