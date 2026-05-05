using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

public sealed class SkiaThumbnailServiceTests : IAsyncLifetime
{
    private DirectoryInfo _tempDir = null!;
    private FileSystemImageStorage _storage = null!;
    private SkiaThumbnailService _thumbnails = null!;

    public Task InitializeAsync()
    {
        _tempDir = TestImageFactory.CreateTempDirectory();
        var options = new StorageOptions { DataDirectory = _tempDir.FullName };
        _storage = new FileSystemImageStorage(options);
        var settings = new JsonAppSettingsService(options, NullLogger<JsonAppSettingsService>.Instance);
        _thumbnails = new SkiaThumbnailService(options, _storage, settings);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_tempDir.Exists)
            _tempDir.Delete(recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Generates_Thumbnail_With_Long_Edge_At_Most_1024()
    {
        var hash = "thumbsrc000000000000000000000000000000000000000000000000000000a1";
        var relative = _storage.BuildRelativePath(hash, ".png");
        using (var src = new MemoryStream(TestImageFactory.CreatePng(4096, 2048)))
            await _storage.SaveAsync(src, relative);

        var result = await _thumbnails.GenerateAsync(relative, hash);

        result.IsError.Should().BeFalse();
        var absolute = _thumbnails.TryResolveAbsolutePath(hash);
        absolute.Should().NotBeNull();
        File.Exists(absolute).Should().BeTrue();

        using var codec = SKCodec.Create(absolute!);
        codec.Should().NotBeNull();
        var info = codec!.Info;
        Math.Max(info.Width, info.Height).Should().Be(1024);
        info.Width.Should().Be(1024);
        info.Height.Should().Be(512);
    }

    [Fact]
    public async Task Returns_Same_Path_On_Second_Generation()
    {
        var hash = "thumbsrc000000000000000000000000000000000000000000000000000000b2";
        var relative = _storage.BuildRelativePath(hash, ".png");
        using (var src = new MemoryStream(TestImageFactory.CreatePng(300, 300)))
            await _storage.SaveAsync(src, relative);

        var first = await _thumbnails.GenerateAsync(relative, hash);
        var second = await _thumbnails.GenerateAsync(relative, hash);

        first.IsError.Should().BeFalse();
        second.IsError.Should().BeFalse();
        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public async Task Returns_NotFound_When_Source_Missing()
    {
        var result = await _thumbnails.GenerateAsync("assets/zz/missing.png", "missinghash");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Preserves_Small_Images_Without_Upscaling()
    {
        var hash = "thumbsrc000000000000000000000000000000000000000000000000000000c3";
        var relative = _storage.BuildRelativePath(hash, ".png");
        using (var src = new MemoryStream(TestImageFactory.CreatePng(100, 64)))
            await _storage.SaveAsync(src, relative);

        var result = await _thumbnails.GenerateAsync(relative, hash);

        result.IsError.Should().BeFalse();
        var absolute = _thumbnails.TryResolveAbsolutePath(hash);
        using var codec = SKCodec.Create(absolute!);
        codec!.Info.Width.Should().Be(100);
        codec.Info.Height.Should().Be(64);
    }
}
