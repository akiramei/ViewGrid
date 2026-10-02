using System.IO.Compression;
using System.Text;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

/// <summary>
/// インポート時の内容検証 (壊れた DB / DB 欠落 / ViewGrid 以外の DB / 画像欠落)。
/// 拒否したときは、展開先を残さず、ワークスペース一覧を変えないことを確認する。
/// </summary>
public sealed class WorkspaceImportValidationTests : IAsyncLifetime
{
    private DirectoryInfo _root = null!;
    private FileSystemWorkspaceManager _manager = null!;

    public Task InitializeAsync()
    {
        _root = TestImageFactory.CreateTempDirectory();
        var workspaceDir = Path.Combine(_root.FullName, WorkspaceBootstrap.WorkspacesSubdirectory,
            WorkspaceBootstrap.DefaultWorkspaceName);
        Directory.CreateDirectory(workspaceDir);
        var ctx = new WorkspaceContext(_root.FullName, workspaceDir, WorkspaceBootstrap.DefaultWorkspaceName);
        _manager = new FileSystemWorkspaceManager(ctx);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _manager.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (_root.Exists)
            _root.Delete(recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>ViewGrid 形式の zip (workspace.json + 指定ファイル) を作る。</summary>
    private string CreateZip(string name, Action<string> populateWorkspace)
    {
        var src = Path.Combine(_root.FullName, $"src-{name}");
        Directory.CreateDirectory(src);
        populateWorkspace(src);

        var zipPath = Path.Combine(_root.FullName, $"{name}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var meta = zip.CreateEntry("workspace.json");
        using (var writer = new StreamWriter(meta.Open(), new UTF8Encoding(false)))
            writer.Write("{\"Name\":\"src\",\"DisplayName\":\"src\"}");
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            zip.CreateEntryFromFile(file, Path.GetRelativePath(src, file).Replace('\\', '/'));
        return zipPath;
    }

    private static void PutImage(string workspaceDir, string relative)
    {
        var full = Path.Combine(workspaceDir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [0x89, 0x50, 0x4e, 0x47]);
    }

    private async Task AssertRejectedAsync(string zipPath, string expectedCode)
    {
        var before = await _manager.ListAsync();

        var result = await _manager.ImportAsync(zipPath, "imported", "取り込み");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(expectedCode);
        Directory.Exists(Path.Combine(_root.FullName, "workspaces", "imported"))
            .Should().BeFalse("拒否したときは展開先を残さない");
        (await _manager.ListAsync()).Should().BeEquivalentTo(before, "ワークスペース一覧 (既存の作業環境) は変わらない");
    }

    [Fact]
    public async Task Import_Accepts_A_Valid_Workspace_And_Registers_It()
    {
        var zip = CreateZip("valid", dir =>
        {
            WorkspaceTestDatabase.Create(Path.Combine(dir, "viewgrid.db"), "assets/ab/abdef.png");
            PutImage(dir, "assets/ab/abdef.png");
        });

        var result = await _manager.ImportAsync(zip, "imported", "取り込み");

        result.IsError.Should().BeFalse();
        (await _manager.ListAsync()).Should().Contain(m => m.Name == "imported");
    }

    [Fact]
    public async Task Import_Rejects_A_Corrupt_Database()
    {
        var zip = CreateZip("corrupt", dir =>
            File.WriteAllText(Path.Combine(dir, "viewgrid.db"), "this is not a sqlite database at all, just text"));

        await AssertRejectedAsync(zip, "Workspace.ImportDatabaseCorrupt");
    }

    [Fact]
    public async Task Import_Rejects_A_Zip_Without_The_Database_Instead_Of_Opening_An_Empty_Workspace()
    {
        var zip = CreateZip("nodb", dir => PutImage(dir, "assets/ab/abdef.png"));

        await AssertRejectedAsync(zip, "Workspace.ImportDatabaseMissing");
    }

    [Fact]
    public async Task Import_Rejects_A_Sqlite_File_That_Is_Not_A_ViewGrid_Database()
    {
        var zip = CreateZip("foreign", dir =>
        {
            var path = Path.Combine(dir, "viewgrid.db");
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Other (Id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        });

        await AssertRejectedAsync(zip, "Workspace.ImportNotViewGridDatabase");
    }

    [Fact]
    public async Task Import_Rejects_When_A_Referenced_Image_Is_Missing_From_The_Zip()
    {
        var zip = CreateZip("noimage", dir =>
            WorkspaceTestDatabase.Create(Path.Combine(dir, "viewgrid.db"), "assets/ab/abdef.png"));

        await AssertRejectedAsync(zip, "Workspace.ImportAssetsMissing");
    }

    [Fact]
    public async Task Import_Rejects_A_Referenced_Image_Path_That_Escapes_The_Workspace()
    {
        var zip = CreateZip("escape", dir =>
            WorkspaceTestDatabase.Create(Path.Combine(dir, "viewgrid.db"), "../outside.png"));
        File.WriteAllBytes(Path.Combine(_root.FullName, "workspaces", "outside.png"), [1]);

        await AssertRejectedAsync(zip, "Workspace.ImportAssetsMissing");
    }
}
