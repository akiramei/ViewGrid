using ErrorOr;
using Microsoft.EntityFrameworkCore;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Infrastructure.Persistence;

namespace ViewGrid.Infrastructure.Repositories;

internal sealed class EfImageCopyRepository(ViewGridDbContext db) : IImageCopyRepository
{
    public async Task<IReadOnlyList<ImageCopy>> FindAllAsync(CancellationToken ct = default) =>
        await db.ImageCopies.AsNoTracking().OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<ImageCopy>> FindByAssetIdAsync(Guid assetId, CancellationToken ct = default) =>
        await db.ImageCopies.AsNoTracking().Where(x => x.AssetId == assetId).ToListAsync(ct);

    public async Task<ImageCopy?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.ImageCopies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<ErrorOr<ImageCopy>> AddAsync(ImageCopy copy, CancellationToken ct = default)
    {
        db.ImageCopies.Add(copy);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(copy).State = EntityState.Detached;
        }
        return copy;
    }

    public async Task<ErrorOr<Success>> UpdateAsync(ImageCopy copy, CancellationToken ct = default)
    {
        var existing = await db.ImageCopies.FirstOrDefaultAsync(x => x.Id == copy.Id, ct);
        if (existing is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {copy.Id} が見つかりません。");

        db.Entry(existing).CurrentValues.SetValues(copy);
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
        var rows = await db.ImageCopies.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0
            ? Result.Success
            : Error.NotFound("ImageCopy.NotFound", $"ImageCopy {id} が見つかりません。");
    }
}
