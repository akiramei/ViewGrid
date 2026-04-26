using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ViewGrid.Core.Entities;
using ViewGrid.Infrastructure.Persistence;
using ViewGrid.Infrastructure.Repositories;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// SQLite in-memory DB + 一時ディレクトリのストレージ一式を束ねるテストフィクスチャ。
/// ユースケーステストからテスト毎に独立した環境として利用する。
/// </summary>
internal sealed class UseCaseFixture : IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public ViewGridDbContext Db { get; }
    public DirectoryInfo TempDir { get; }
    public StorageOptions StorageOptions { get; }
    public FileSystemImageStorage Storage { get; }
    public SkiaThumbnailService Thumbnails { get; }
    public EfImageAssetRepository AssetRepository { get; }
    public EfImageCopyRepository CopyRepository { get; }
    public EfGridCanvasRepository GridRepository { get; }
    public EfGridPlacementRepository PlacementRepository { get; }

    private UseCaseFixture(
        SqliteConnection connection,
        ViewGridDbContext db,
        DirectoryInfo tempDir,
        StorageOptions options,
        FileSystemImageStorage storage,
        SkiaThumbnailService thumbnails,
        EfImageAssetRepository assetRepository,
        EfImageCopyRepository copyRepository,
        EfGridCanvasRepository gridRepository,
        EfGridPlacementRepository placementRepository)
    {
        Connection = connection;
        Db = db;
        TempDir = tempDir;
        StorageOptions = options;
        Storage = storage;
        Thumbnails = thumbnails;
        AssetRepository = assetRepository;
        CopyRepository = copyRepository;
        GridRepository = gridRepository;
        PlacementRepository = placementRepository;
    }

    public static async Task<UseCaseFixture> CreateAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ViewGridDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ViewGridDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tempDir = TestImageFactory.CreateTempDirectory();
        var storageOptions = new StorageOptions { DataDirectory = tempDir.FullName };
        var storage = new FileSystemImageStorage(storageOptions);
        var thumbnails = new SkiaThumbnailService(storageOptions, storage);

        return new UseCaseFixture(
            connection,
            db,
            tempDir,
            storageOptions,
            storage,
            thumbnails,
            new EfImageAssetRepository(db),
            new EfImageCopyRepository(db),
            new EfGridCanvasRepository(db),
            new EfGridPlacementRepository(db));
    }

    /// <summary>
    /// 実ファイルつきの <see cref="ImageAsset"/> を投入する。
    /// </summary>
    public async Task<ImageAsset> SeedAssetAsync(
        string fileHash = "hash000000000000000000000000000000000000000000000000000000000001",
        int width = 100,
        int height = 100)
    {
        var relative = Storage.BuildRelativePath(fileHash, ".png");
        using (var src = new MemoryStream(TestImageFactory.CreatePng(width, height)))
            await Storage.SaveAsync(src, relative);

        var asset = new ImageAsset
        {
            Id = Guid.NewGuid(),
            SourceType = ImageSource.File,
            OriginalFilename = $"seed-{fileHash[..8]}.png",
            StoredRelativePath = relative,
            Size = new PixelSize(width, height),
            FileHash = fileHash,
            FileSizeBytes = new FileInfo(Storage.ResolveAbsolutePath(relative)).Length,
            MimeType = "image/png",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await AssetRepository.AddAsync(asset);
        if (result.IsError)
            throw new InvalidOperationException($"SeedAssetAsync failed: {string.Join(", ", result.Errors)}");
        return asset;
    }

    /// <summary>
    /// 既定値の <see cref="ImageCopy"/> を投入する。
    /// </summary>
    public async Task<ImageCopy> SeedCopyAsync(Guid assetId, string? copyName = null)
    {
        var now = DateTimeOffset.UtcNow;
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            CopyName = copyName,
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            TrimmingAnchor = TrimmingAnchor.Center,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var result = await CopyRepository.AddAsync(copy);
        if (result.IsError)
            throw new InvalidOperationException($"SeedCopyAsync failed: {string.Join(", ", result.Errors)}");
        return copy;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Connection.DisposeAsync();
        if (TempDir.Exists)
            TempDir.Delete(recursive: true);
    }
}
