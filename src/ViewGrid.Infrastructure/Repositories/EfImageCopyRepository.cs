using System.Collections.Immutable;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Infrastructure.Persistence;

namespace ViewGrid.Infrastructure.Repositories;

/// <summary>
/// <see cref="ImageCopy"/> の永続化を担当する EF Core Repository。
/// </summary>
/// <remarks>
/// <para><b>Regions の取り扱い</b>:</para>
/// <see cref="ImageCopy.Regions"/> は <c>image_copy_regions</c> 子テーブルにマップした
/// 独立 entity (<see cref="ProtectedRegion"/>) として保存する。 集約ビューとしてはアプリケーション
/// 層に <see cref="ImageCopy.Regions"/> 経由で見えるが、 EF 上は分離されているため:
/// <list type="bullet">
///   <item>Find 系は ImageCopies と ProtectedRegions を別クエリで読み、 アプリ側で結合して
///     <see cref="ImageCopy.Regions"/> に attach する</item>
///   <item><see cref="AddAsync"/> は ImageCopy 本体と Regions の両方を同一 SaveChanges に積む</item>
///   <item><see cref="UpdateAsync"/> は Region の Id 安定性を保つため delete-all-then-add ではなく
///     差分同期 (Add / Update / Remove) を行う。 Id を持つ Region は UI / Undo 側からの
///     参照キーとして安定している必要がある</item>
///   <item>削除は ImageCopy 削除時に FK cascade で連動するため Repository 側に追加処理は不要</item>
/// </list>
/// </remarks>
internal sealed class EfImageCopyRepository(ViewGridDbContext db) : IImageCopyRepository
{
    public async Task<IReadOnlyList<ImageCopy>> FindAllAsync(CancellationToken ct = default)
    {
        var copies = await db.ImageCopies.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
        if (copies.Count == 0) return copies;
        var regionsByCopyId = await LoadRegionsByCopyIdAsync(copies.Select(c => c.Id), ct);
        AttachRegions(copies, regionsByCopyId);
        return copies;
    }

    public async Task<IReadOnlyList<ImageCopy>> FindByAssetIdAsync(Guid assetId, CancellationToken ct = default)
    {
        var copies = await db.ImageCopies.AsNoTracking()
            .Where(x => x.AssetId == assetId)
            .ToListAsync(ct);
        if (copies.Count == 0) return copies;
        var regionsByCopyId = await LoadRegionsByCopyIdAsync(copies.Select(c => c.Id), ct);
        AttachRegions(copies, regionsByCopyId);
        return copies;
    }

    public async Task<ImageCopy?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var copy = await db.ImageCopies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (copy is null) return null;
        var regions = await db.ProtectedRegions.AsNoTracking()
            .Where(r => r.ImageCopyId == id)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);
        if (regions.Count > 0) copy.Regions = regions.ToImmutableArray();
        return copy;
    }

    public async Task<ErrorOr<ImageCopy>> AddAsync(ImageCopy copy, CancellationToken ct = default)
    {
        db.ImageCopies.Add(copy);
        var regionsToTrack = copy.Regions.IsDefaultOrEmpty
            ? Array.Empty<ProtectedRegion>()
            : copy.Regions.ToArray();
        if (regionsToTrack.Length > 0)
            db.ProtectedRegions.AddRange(regionsToTrack);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(copy).State = EntityState.Detached;
            DetachAll(regionsToTrack);
        }
        return copy;
    }

    public async Task<ErrorOr<Success>> UpdateAsync(ImageCopy copy, CancellationToken ct = default)
    {
        var existing = await db.ImageCopies.FirstOrDefaultAsync(x => x.Id == copy.Id, ct);
        if (existing is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {copy.Id} が見つかりません。");

        db.Entry(existing).CurrentValues.SetValues(copy);

        // 既存 Regions を tracking でロードし、 Id ベースで差分同期する。
        // Id 不一致 = 削除、 Id 一致 = SetValues、 新規 Id = 追加。
        var existingRegions = await db.ProtectedRegions
            .Where(r => r.ImageCopyId == copy.Id)
            .ToListAsync(ct);
        var newlyAdded = SyncRegions(existingRegions, copy.Regions);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(existing).State = EntityState.Detached;
            DetachAll(existingRegions);
            DetachAll(newlyAdded);
        }
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // image_copy_regions は FK ON DELETE CASCADE で連動削除されるため、
        // ImageCopy 行を 1 度の ExecuteDelete で消すだけでよい。
        var rows = await db.ImageCopies.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0
            ? Result.Success
            : Error.NotFound("ImageCopy.NotFound", $"ImageCopy {id} が見つかりません。");
    }

    /// <summary>複数の <see cref="ImageCopy.Id"/> に紐付く Regions を 1 クエリで取得し、 copyId でグルーピング。</summary>
    private async Task<Dictionary<Guid, ImmutableArray<ProtectedRegion>>> LoadRegionsByCopyIdAsync(
        IEnumerable<Guid> copyIds, CancellationToken ct)
    {
        var ids = copyIds as IReadOnlyCollection<Guid> ?? copyIds.ToArray();
        if (ids.Count == 0) return [];
        var rows = await db.ProtectedRegions.AsNoTracking()
            .Where(r => ids.Contains(r.ImageCopyId))
            .OrderBy(r => r.ImageCopyId)
            .ThenBy(r => r.SortOrder)
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.ImageCopyId)
            .ToDictionary(g => g.Key, g => g.ToImmutableArray());
    }

    private static void AttachRegions(
        IReadOnlyList<ImageCopy> copies,
        Dictionary<Guid, ImmutableArray<ProtectedRegion>> regionsByCopyId)
    {
        foreach (var copy in copies)
            if (regionsByCopyId.TryGetValue(copy.Id, out var arr) && !arr.IsEmpty)
                copy.Regions = arr;
    }

    /// <summary>
    /// 既存 Regions と desired Regions を Id で突き合わせて Add / Update / Remove する。
    /// 戻り値は新規追加した entity (finally で Detach するため)。
    /// </summary>
    private List<ProtectedRegion> SyncRegions(
        IReadOnlyList<ProtectedRegion> existing,
        ImmutableArray<ProtectedRegion> desired)
    {
        var desiredById = desired.IsDefaultOrEmpty
            ? new Dictionary<Guid, ProtectedRegion>()
            : desired.ToDictionary(r => r.Id);
        var existingIds = existing.Select(r => r.Id).ToHashSet();

        // Update or Remove 既存
        foreach (var ex in existing)
        {
            if (desiredById.TryGetValue(ex.Id, out var d))
                db.Entry(ex).CurrentValues.SetValues(d);
            else
                db.ProtectedRegions.Remove(ex);
        }

        // Add 新規
        var newlyAdded = new List<ProtectedRegion>();
        if (!desired.IsDefaultOrEmpty)
        {
            foreach (var d in desired)
            {
                if (!existingIds.Contains(d.Id))
                {
                    db.ProtectedRegions.Add(d);
                    newlyAdded.Add(d);
                }
            }
        }
        return newlyAdded;
    }

    private void DetachAll(IEnumerable<ProtectedRegion> regions)
    {
        foreach (var r in regions)
        {
            var entry = db.Entry(r);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
        }
    }
}
