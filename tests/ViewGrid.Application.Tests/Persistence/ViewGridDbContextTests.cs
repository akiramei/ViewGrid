using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ViewGrid.Core.Entities;
using ViewGrid.Infrastructure.Persistence;
using ViewGrid.Infrastructure.Repositories;

namespace ViewGrid.Application.Tests.Persistence;

/// <summary>
/// SQLite の in-memory モードで EF Core マッピングとリポジトリを検証する。
/// </summary>
public sealed class ViewGridDbContextTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ViewGridDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ViewGridDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ViewGridDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ImageAsset_Roundtrip_Preserves_All_Fields()
    {
        var repo = new EfImageAssetRepository(_db);
        var now = DateTimeOffset.UtcNow;
        var asset = new ImageAsset
        {
            Id = Guid.NewGuid(),
            SourceType = ImageSource.File,
            OriginalFilename = "sample.png",
            StoredRelativePath = "assets/ab/abcdef.png",
            Size = new PixelSize(1920, 1080),
            FileHash = "abcdef",
            FileSizeBytes = 123456L,
            MimeType = "image/png",
            CreatedAt = now,
        };

        var addResult = await repo.AddAsync(asset);
        addResult.IsError.Should().BeFalse();

        var reloaded = await repo.FindByIdAsync(asset.Id);
        reloaded.Should().NotBeNull();
        reloaded!.OriginalFilename.Should().Be("sample.png");
        reloaded.Size.Width.Should().Be(1920);
        reloaded.Size.Height.Should().Be(1080);
        reloaded.SourceType.Should().Be(ImageSource.File);
    }

    [Fact]
    public async Task ImageAsset_Duplicate_Hash_Returns_Conflict()
    {
        var repo = new EfImageAssetRepository(_db);
        var asset = BuildAsset("same-hash");
        await repo.AddAsync(asset);

        var duplicate = BuildAsset("same-hash");
        var result = await repo.AddAsync(duplicate);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task ImageCopy_Preserves_ValueObjects_Through_Roundtrip()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);

        var asset = BuildAsset("copy-parent");
        await assetRepo.AddAsync(asset);

        var now = DateTimeOffset.UtcNow;
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            CopyName = "rotated-90",
            Transform = new ImageTransform(Rotation.Cw90, true, false),
            ScalingMode = ScalingMode.UniformContainShrinkOnly,
            Alignment = new Alignment(AnchorX.Right, AnchorY.Bottom),
            OccupySize = new OccupySize(2, 1),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await copyRepo.AddAsync(copy);

        var reloaded = await copyRepo.FindByIdAsync(copy.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Transform.Rotation.Should().Be(Rotation.Cw90);
        reloaded.Transform.FlipX.Should().BeTrue();
        reloaded.ScalingMode.Should().Be(ScalingMode.UniformContainShrinkOnly);
        reloaded.Alignment.X.Should().Be(AnchorX.Right);
        reloaded.Alignment.Y.Should().Be(AnchorY.Bottom);
        reloaded.OccupySize.Width.Should().Be(2);
        reloaded.OccupySize.Height.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsset_Cascades_To_Copies()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);

        var asset = BuildAsset("cascade-parent");
        await assetRepo.AddAsync(asset);

        var copy = BuildCopy(asset.Id);
        await copyRepo.AddAsync(copy);

        await assetRepo.DeleteAsync(asset.Id);

        var remaining = await copyRepo.FindByAssetIdAsync(asset.Id);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ImageCopy_With_Regions_Roundtrip_Preserves_Order_And_Values()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);
        var asset = BuildAsset("regions-roundtrip");
        await assetRepo.AddAsync(asset);

        var copyId = Guid.NewGuid();
        var regionA = new ProtectedRegion
        {
            Id = Guid.NewGuid(),
            ImageCopyId = copyId,
            Rect = new RegionRectFraction(0.1, 0.2, 0.3, 0.4),
            FillMode = ProtectedRegionFillMode.White,
            SortOrder = 0,
        };
        var regionB = new ProtectedRegion
        {
            Id = Guid.NewGuid(),
            ImageCopyId = copyId,
            Rect = new RegionRectFraction(0.5, 0.5, 0.2, 0.2),
            FillMode = ProtectedRegionFillMode.White,
            SortOrder = 1,
        };
        var copy = new ImageCopy
        {
            Id = copyId,
            AssetId = asset.Id,
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Regions = ImmutableArray.Create(regionA, regionB),
        };

        await copyRepo.AddAsync(copy);

        var reloaded = await copyRepo.FindByIdAsync(copyId);
        reloaded.Should().NotBeNull();
        reloaded!.Regions.Should().HaveCount(2);
        reloaded.Regions[0].Id.Should().Be(regionA.Id);
        reloaded.Regions[0].Rect.Should().Be(regionA.Rect);
        reloaded.Regions[0].SortOrder.Should().Be(0);
        reloaded.Regions[1].Id.Should().Be(regionB.Id);
        reloaded.Regions[1].SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task FindByAssetId_Loads_Regions_For_All_Copies()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);
        var asset = BuildAsset("regions-multi");
        await assetRepo.AddAsync(asset);

        var copy1 = BuildCopyWithRegions(asset.Id, regionCount: 1);
        var copy2 = BuildCopyWithRegions(asset.Id, regionCount: 3);
        var copy3NoRegions = BuildCopy(asset.Id);
        await copyRepo.AddAsync(copy1);
        await copyRepo.AddAsync(copy2);
        await copyRepo.AddAsync(copy3NoRegions);

        var loaded = await copyRepo.FindByAssetIdAsync(asset.Id);
        loaded.Should().HaveCount(3);
        loaded.Single(c => c.Id == copy1.Id).Regions.Should().HaveCount(1);
        loaded.Single(c => c.Id == copy2.Id).Regions.Should().HaveCount(3);
        loaded.Single(c => c.Id == copy3NoRegions.Id).Regions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_Diff_Syncs_Regions()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);
        var asset = BuildAsset("regions-update");
        await assetRepo.AddAsync(asset);

        var initial = BuildCopyWithRegions(asset.Id, regionCount: 2);
        await copyRepo.AddAsync(initial);

        // Region A (Id 維持、 Rect 変更) + 新 Region C (削除した B の代わり)。
        var modifiedA = new ProtectedRegion
        {
            Id = initial.Regions[0].Id,
            ImageCopyId = initial.Id,
            Rect = new RegionRectFraction(0.0, 0.0, 0.99, 0.99),
            FillMode = ProtectedRegionFillMode.White,
            SortOrder = 0,
        };
        var newC = new ProtectedRegion
        {
            Id = Guid.NewGuid(),
            ImageCopyId = initial.Id,
            Rect = new RegionRectFraction(0.4, 0.4, 0.1, 0.1),
            FillMode = ProtectedRegionFillMode.White,
            SortOrder = 1,
        };
        var updated = new ImageCopy
        {
            Id = initial.Id,
            AssetId = initial.AssetId,
            Transform = initial.Transform,
            ScalingMode = initial.ScalingMode,
            Alignment = initial.Alignment,
            OccupySize = initial.OccupySize,
            CreatedAt = initial.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            Regions = ImmutableArray.Create(modifiedA, newC),
        };

        var result = await copyRepo.UpdateAsync(updated);
        result.IsError.Should().BeFalse();

        var reloaded = await copyRepo.FindByIdAsync(initial.Id);
        reloaded!.Regions.Should().HaveCount(2);
        // A は Id 安定 + Rect 更新
        var loadedA = reloaded.Regions.Single(r => r.Id == modifiedA.Id);
        loadedA.Rect.Width.Should().BeApproximately(0.99, 1e-9);
        // 旧 B は削除されている
        reloaded.Regions.Should().NotContain(r => r.Id == initial.Regions[1].Id);
        // 新 C が追加されている
        reloaded.Regions.Should().Contain(r => r.Id == newC.Id);
    }

    [Fact]
    public async Task DeleteCopy_Cascades_To_Regions()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);
        var asset = BuildAsset("regions-cascade");
        await assetRepo.AddAsync(asset);

        var copy = BuildCopyWithRegions(asset.Id, regionCount: 3);
        await copyRepo.AddAsync(copy);

        await copyRepo.DeleteAsync(copy.Id);

        // FK ON DELETE CASCADE で連動削除されているはず。 直接 DbSet を覗いて確認。
        var remaining = await _db.ProtectedRegions.AsNoTracking()
            .Where(r => r.ImageCopyId == copy.Id)
            .CountAsync();
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsset_Cascades_To_Copies_And_Regions()
    {
        var assetRepo = new EfImageAssetRepository(_db);
        var copyRepo = new EfImageCopyRepository(_db);
        var asset = BuildAsset("regions-cascade-2level");
        await assetRepo.AddAsync(asset);

        var copy = BuildCopyWithRegions(asset.Id, regionCount: 2);
        await copyRepo.AddAsync(copy);

        await assetRepo.DeleteAsync(asset.Id);

        var copiesGone = await _db.ImageCopies.AsNoTracking().AnyAsync();
        copiesGone.Should().BeFalse();
        var regionsGone = await _db.ProtectedRegions.AsNoTracking().AnyAsync();
        regionsGone.Should().BeFalse();
    }

    private static ImageAsset BuildAsset(string hash) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = ImageSource.File,
        OriginalFilename = "x.png",
        StoredRelativePath = $"assets/{hash}.png",
        Size = new PixelSize(100, 100),
        FileHash = hash,
        FileSizeBytes = 1024,
        MimeType = "image/png",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ImageCopy BuildCopy(Guid assetId) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = assetId,
        Transform = ImageTransform.Identity,
        ScalingMode = ScalingMode.UniformContain,
        Alignment = Alignment.Center,
        OccupySize = OccupySize.OneByOne,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static ImageCopy BuildCopyWithRegions(Guid assetId, int regionCount)
    {
        var copyId = Guid.NewGuid();
        var regions = Enumerable.Range(0, regionCount)
            .Select(i => new ProtectedRegion
            {
                Id = Guid.NewGuid(),
                ImageCopyId = copyId,
                Rect = new RegionRectFraction(0.1 * i, 0.1 * i, 0.2, 0.2),
                FillMode = ProtectedRegionFillMode.White,
                SortOrder = i,
            })
            .ToImmutableArray();
        return new ImageCopy
        {
            Id = copyId,
            AssetId = assetId,
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Regions = regions,
        };
    }
}
