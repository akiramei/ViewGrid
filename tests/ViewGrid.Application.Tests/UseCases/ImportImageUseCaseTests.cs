using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Infrastructure.Persistence;
using ViewGrid.Infrastructure.Repositories;
using ViewGrid.Infrastructure.Services;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class ImportImageUseCaseTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ViewGridDbContext _db = null!;
    private DirectoryInfo _tempDir = null!;
    private ImportImageUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ViewGridDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ViewGridDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _tempDir = TestImageFactory.CreateTempDirectory();
        var storageOptions = new StorageOptions { DataDirectory = _tempDir.FullName };
        var storage = new FileSystemImageStorage(storageOptions);
        var thumbnails = new SkiaThumbnailService(storageOptions, storage);

        _useCase = new ImportImageUseCase(
            hasher: new Sha256ImageHasher(),
            prober: new SkiaImageProber(),
            storage: storage,
            thumbnailService: thumbnails,
            assetRepository: new EfImageAssetRepository(_db),
            copyRepository: new EfImageCopyRepository(_db),
            logger: NullLogger<ImportImageUseCase>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        if (_tempDir.Exists)
            _tempDir.Delete(recursive: true);
    }

    [Fact]
    public async Task Imports_New_Image_And_Creates_Default_Copy()
    {
        var file = TestImageFactory.WritePngToTempFile(200, 150);
        try
        {
            var result = await _useCase.ExecuteAsync(new ImportImageRequest { SourcePath = file });

            result.IsError.Should().BeFalse();
            var value = result.Value;
            value.WasDuplicate.Should().BeFalse();
            value.Asset.Size.Width.Should().Be(200);
            value.Asset.Size.Height.Should().Be(150);
            value.Asset.MimeType.Should().Be("image/png");
            value.Asset.FileHash.Should().MatchRegex("^[0-9a-f]{64}$");
            value.Asset.StoredRelativePath.Should().StartWith("assets/");
            value.DefaultCopy.AssetId.Should().Be(value.Asset.Id);
            value.DefaultCopy.Transform.Should().Be(ImageTransform.Identity);
            value.DefaultCopy.OccupySize.Should().Be(OccupySize.OneByOne);

            File.Exists(Path.Combine(_tempDir.FullName, value.Asset.StoredRelativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Returns_Existing_Asset_On_Duplicate_Hash()
    {
        var file = TestImageFactory.WritePngToTempFile(64, 64);
        try
        {
            var first = await _useCase.ExecuteAsync(new ImportImageRequest { SourcePath = file });
            var second = await _useCase.ExecuteAsync(new ImportImageRequest { SourcePath = file });

            first.IsError.Should().BeFalse();
            second.IsError.Should().BeFalse();
            second.Value.WasDuplicate.Should().BeTrue();
            second.Value.Asset.Id.Should().Be(first.Value.Asset.Id);
            second.Value.DefaultCopy.Id.Should().Be(first.Value.DefaultCopy.Id);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Source_File()
    {
        var result = await _useCase.ExecuteAsync(new ImportImageRequest
        {
            SourcePath = Path.Combine(Path.GetTempPath(), "never-exists-12345.png"),
        });

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Returns_Validation_Error_For_Non_Image_Bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewgrid-test-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(path, "not an image");
        try
        {
            var result = await _useCase.ExecuteAsync(new ImportImageRequest { SourcePath = path });

            result.IsError.Should().BeTrue();
            result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
