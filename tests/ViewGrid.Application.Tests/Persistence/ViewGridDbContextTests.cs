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
    public async Task GridCanvas_SetActive_Ensures_Single_Active_Grid()
    {
        var repo = new EfGridCanvasRepository(_db);

        var g1 = BuildGrid("A", isActive: true);
        var g2 = BuildGrid("B", isActive: false);
        var g3 = BuildGrid("C", isActive: false);
        await repo.AddAsync(g1);
        await repo.AddAsync(g2);
        await repo.AddAsync(g3);

        var result = await repo.SetActiveAsync(g2.Id);
        result.IsError.Should().BeFalse();

        var active = await repo.FindActiveAsync();
        active.Should().NotBeNull();
        active!.Id.Should().Be(g2.Id);

        var all = await repo.FindAllAsync();
        all.Count(x => x.IsActive).Should().Be(1);
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

    private static GridCanvas BuildGrid(string name, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        GridRows = 3,
        GridCols = 3,
        ColWeights = GridCanvas.UniformWeights(3),
        RowWeights = GridCanvas.UniformWeights(3),
        CanvasSize = new PixelSize(1200, 1200),
        IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
