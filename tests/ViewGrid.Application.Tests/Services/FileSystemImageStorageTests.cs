using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

public sealed class FileSystemImageStorageTests : IAsyncLifetime
{
    private DirectoryInfo _tempDir = null!;
    private FileSystemImageStorage _storage = null!;

    public Task InitializeAsync()
    {
        _tempDir = TestImageFactory.CreateTempDirectory();
        _storage = new FileSystemImageStorage(new StorageOptions { DataDirectory = _tempDir.FullName });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_tempDir.Exists)
            _tempDir.Delete(recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public void BuildRelativePath_Shards_By_First_Two_Hash_Characters()
    {
        var path = _storage.BuildRelativePath("abcdef0123456789", ".png");
        path.Should().Be("assets/ab/abcdef0123456789.png");
    }

    [Fact]
    public void BuildRelativePath_Accepts_Extension_With_Or_Without_Dot()
    {
        var withDot = _storage.BuildRelativePath("deadbeef", ".jpg");
        var withoutDot = _storage.BuildRelativePath("deadbeef", "jpg");
        withDot.Should().Be(withoutDot);
    }

    [Fact]
    public async Task SaveAsync_Persists_Content_And_Creates_Intermediate_Dirs()
    {
        using var source = new MemoryStream(TestImageFactory.CreatePng(10, 10));
        var relative = _storage.BuildRelativePath("ab1234567890", ".png");

        await _storage.SaveAsync(source, relative);

        _storage.Exists(relative).Should().BeTrue();
        File.ReadAllBytes(_storage.ResolveAbsolutePath(relative)).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SaveAsync_Skips_When_File_Already_Exists()
    {
        var relative = _storage.BuildRelativePath("feedface00", ".png");

        using (var first = new MemoryStream([1, 2, 3]))
            await _storage.SaveAsync(first, relative);

        using (var second = new MemoryStream([9, 9, 9, 9, 9]))
            await _storage.SaveAsync(second, relative);

        var content = File.ReadAllBytes(_storage.ResolveAbsolutePath(relative));
        content.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Delete_Removes_Existing_File_And_Is_NoOp_Otherwise()
    {
        var relative = _storage.BuildRelativePath("cafebabe00", ".png");
        using (var src = new MemoryStream([1, 2, 3]))
            await _storage.SaveAsync(src, relative);

        _storage.Delete(relative);
        _storage.Exists(relative).Should().BeFalse();

        // 二度目の削除は例外を投げない
        _storage.Delete(relative);
    }
}
