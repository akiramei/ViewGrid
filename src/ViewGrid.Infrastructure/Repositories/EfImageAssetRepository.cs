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

internal sealed class EfImageAssetRepository(ViewGridDbContext db) : IImageAssetRepository
{
    public async Task<IReadOnlyList<ImageAsset>> FindAllAsync(CancellationToken ct = default) =>
        await db.ImageAssets
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<ImageAsset?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.ImageAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<ImageAsset?> FindByHashAsync(string fileHash, CancellationToken ct = default) =>
        await db.ImageAssets.AsNoTracking().FirstOrDefaultAsync(x => x.FileHash == fileHash, ct);

    public async Task<ErrorOr<ImageAsset>> AddAsync(ImageAsset asset, CancellationToken ct = default)
    {
        db.ImageAssets.Add(asset);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Error.Conflict("ImageAsset.Duplicate", "同一ハッシュの画像が既に存在します。");
        }
        finally
        {
            db.Entry(asset).State = EntityState.Detached;
        }
        return asset;
    }

    public async Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await db.ImageAssets.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0
            ? Result.Success
            : Error.NotFound("ImageAsset.NotFound", $"ImageAsset {id} が見つかりません。");
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
}
