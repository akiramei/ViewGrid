using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

/// <summary>
/// <see cref="FileSystemWorkspaceManager"/> の一覧 / 作成 / リネーム / 削除 / 切替を検証する。
/// 各テストは一時 RootDirectory 上で完結する。
/// </summary>
public sealed class FileSystemWorkspaceManagerTests : IAsyncLifetime
{
    private DirectoryInfo _root = null!;
    private string _workspaceDir = null!;
    private FileSystemWorkspaceManager _manager = null!;

    public Task InitializeAsync()
    {
        _root = TestImageFactory.CreateTempDirectory();
        _workspaceDir = Path.Combine(_root.FullName, WorkspaceBootstrap.WorkspacesSubdirectory,
            WorkspaceBootstrap.DefaultWorkspaceName);
        Directory.CreateDirectory(_workspaceDir);

        var ctx = new WorkspaceContext(_root.FullName, _workspaceDir, WorkspaceBootstrap.DefaultWorkspaceName);
        _manager = new FileSystemWorkspaceManager(ctx);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ListAsync_Falls_Back_To_Active_When_Manifest_Missing()
    {
        var entries = await _manager.ListAsync();
        entries.Should().ContainSingle();
        entries[0].Name.Should().Be(WorkspaceBootstrap.DefaultWorkspaceName);
    }

    [Fact]
    public async Task CreateAsync_Adds_Workspace_To_Manifest_And_Creates_Directory()
    {
        var result = await _manager.CreateAsync("work", "仕事用");

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("work");
        result.Value.DisplayName.Should().Be("仕事用");
        Directory.Exists(Path.Combine(_root.FullName, "workspaces", "work")).Should().BeTrue();

        var entries = await _manager.ListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(m => m.Name == "work" && m.DisplayName == "仕事用");
    }

    [Theory]
    [InlineData("invalid space")]
    [InlineData("with/slash")]
    [InlineData("")]
    public async Task CreateAsync_Rejects_Invalid_Name(string name)
    {
        var result = await _manager.CreateAsync(name, "表示名");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task CreateAsync_Rejects_Duplicate_Name()
    {
        await _manager.CreateAsync("work", "仕事用");
        var second = await _manager.CreateAsync("work", "別表示");

        second.IsError.Should().BeTrue();
        second.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task RenameAsync_Updates_DisplayName_Only()
    {
        await _manager.CreateAsync("hobby", "趣味");

        var result = await _manager.RenameAsync("hobby", "プライベート");

        result.IsError.Should().BeFalse();
        var entries = await _manager.ListAsync();
        entries.Should().Contain(m => m.Name == "hobby" && m.DisplayName == "プライベート");
    }

    [Fact]
    public async Task DeleteAsync_Moves_Workspace_To_Trash_And_Removes_From_Manifest()
    {
        await _manager.CreateAsync("temp", "一時");

        var result = await _manager.DeleteAsync("temp");

        result.IsError.Should().BeFalse();
        Directory.Exists(Path.Combine(_root.FullName, "workspaces", "temp")).Should().BeFalse();
        var trashRoot = Path.Combine(_root.FullName, "workspaces", ".trash");
        Directory.Exists(trashRoot).Should().BeTrue();
        Directory.GetDirectories(trashRoot).Should().Contain(p => Path.GetFileName(p).StartsWith("temp-"));

        var entries = await _manager.ListAsync();
        entries.Should().NotContain(m => m.Name == "temp");
    }

    [Fact]
    public async Task DeleteAsync_Rejects_Active_Workspace()
    {
        var result = await _manager.DeleteAsync(WorkspaceBootstrap.DefaultWorkspaceName);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task SetActiveAsync_Writes_ActiveJson()
    {
        await _manager.CreateAsync("work", "仕事用");

        var result = await _manager.SetActiveAsync("work");

        result.IsError.Should().BeFalse();
        var activePath = Path.Combine(_root.FullName, "active.json");
        File.Exists(activePath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(activePath);
        content.Should().Contain("\"work\"");
    }

    [Fact]
    public async Task DuplicateAsync_Copies_Files_And_Adds_To_Manifest()
    {
        // Default ワークスペースに DB と assets を仕込む
        var dbPath = Path.Combine(_workspaceDir, "viewgrid.db");
        await File.WriteAllTextAsync(dbPath, "fake-db-content");
        var assetsDir = Path.Combine(_workspaceDir, "assets", "ab");
        Directory.CreateDirectory(assetsDir);
        var assetPath = Path.Combine(assetsDir, "abdef.png");
        await File.WriteAllBytesAsync(assetPath, [0x89, 0x50, 0x4e, 0x47]);

        var result = await _manager.DuplicateAsync(WorkspaceBootstrap.DefaultWorkspaceName,
            "default-copy", "Default のコピー");

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("default-copy");

        var copiedDb = Path.Combine(_root.FullName, "workspaces", "default-copy", "viewgrid.db");
        File.Exists(copiedDb).Should().BeTrue();
        (await File.ReadAllTextAsync(copiedDb)).Should().Be("fake-db-content");

        var copiedAsset = Path.Combine(_root.FullName, "workspaces", "default-copy", "assets", "ab", "abdef.png");
        File.Exists(copiedAsset).Should().BeTrue();

        var entries = await _manager.ListAsync();
        entries.Should().Contain(m => m.Name == "default-copy" && m.DisplayName == "Default のコピー");
    }

    [Fact]
    public async Task DuplicateAsync_Rejects_Duplicate_Target_Name()
    {
        await _manager.CreateAsync("work", "仕事用");

        var result = await _manager.DuplicateAsync(WorkspaceBootstrap.DefaultWorkspaceName,
            "work", "重複");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task DuplicateAsync_Rejects_Missing_Source()
    {
        var result = await _manager.DuplicateAsync("nonexistent", "copy", "コピー");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task SetActiveAsync_Rejects_Missing_Workspace()
    {
        var result = await _manager.SetActiveAsync("nonexistent");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }
}
