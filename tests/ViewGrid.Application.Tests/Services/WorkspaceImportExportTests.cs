using System.IO.Compression;
using System.Text;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

/// <summary>
/// <see cref="FileSystemWorkspaceManager.ExportAsync"/> と
/// <see cref="FileSystemWorkspaceManager.ImportAsync"/> のラウンドトリップ / 境界条件を検証する。
/// </summary>
public sealed class WorkspaceImportExportTests : IAsyncLifetime
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

    private string CreateZipPath(string name) => Path.Combine(_root.FullName, $"{name}.zip");

    private async Task SeedDefaultWorkspaceAsync()
    {
        var dbPath = Path.Combine(_workspaceDir, "viewgrid.db");
        await File.WriteAllTextAsync(dbPath, "fake-db-content");
        var assetsDir = Path.Combine(_workspaceDir, "assets", "ab");
        Directory.CreateDirectory(assetsDir);
        await File.WriteAllBytesAsync(Path.Combine(assetsDir, "abdef.png"), [0x89, 0x50, 0x4e, 0x47]);
    }

    [Fact]
    public async Task ExportAsync_CreatesZip_WithMetadataAndFiles()
    {
        await SeedDefaultWorkspaceAsync();
        var zipPath = CreateZipPath("default-export");

        var result = await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath);

        result.IsError.Should().BeFalse();
        File.Exists(zipPath).Should().BeTrue();

        using var zip = ZipFile.OpenRead(zipPath);
        zip.GetEntry("workspace.json").Should().NotBeNull();
        zip.GetEntry("viewgrid.db").Should().NotBeNull();
        zip.GetEntry("assets/ab/abdef.png").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportAsync_ExcludesLockFile()
    {
        await SeedDefaultWorkspaceAsync();
        await File.WriteAllTextAsync(Path.Combine(_workspaceDir, WorkspaceLock.LockFileName), "12345");

        var zipPath = CreateZipPath("default-export");
        var result = await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath);
        result.IsError.Should().BeFalse();

        using var zip = ZipFile.OpenRead(zipPath);
        zip.GetEntry(WorkspaceLock.LockFileName).Should().BeNull();
    }

    [Fact]
    public async Task ExportAsync_OverwritesExistingZip()
    {
        await SeedDefaultWorkspaceAsync();
        var zipPath = CreateZipPath("default-export");
        await File.WriteAllTextAsync(zipPath, "stale-content-not-a-zip");

        var result = await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath);

        result.IsError.Should().BeFalse();
        // 既存ファイルは上書きされ、 中身は zip としてパース可能になっているはず
        using var zip = ZipFile.OpenRead(zipPath);
        zip.GetEntry("workspace.json").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportAsync_RejectsInvalidName()
    {
        var result = await _manager.ExportAsync("with space", CreateZipPath("invalid"));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task ExportAsync_RejectsMissingSource()
    {
        var result = await _manager.ExportAsync("nonexistent", CreateZipPath("missing"));

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task ExportAsync_RejectsTargetInsideSourceDirectory()
    {
        // 出力先がソースディレクトリ内だと、 列挙中に自身を取り込もうとして race するため拒否する。
        await SeedDefaultWorkspaceAsync();
        var insideZip = Path.Combine(_workspaceDir, "self-export.zip");

        var result = await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, insideZip);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task ExportAsync_SucceedsWhileSourceFileIsOpenForRead()
    {
        // SQLite が viewgrid.db を FileShare.ReadWrite|Delete で開いた状態を模擬する。
        // ZipArchive 既定の FileShare.Read だと共有違反で失敗するが、 自前ヘルパが
        // FileShare.ReadWrite で開くため成功する想定。
        await SeedDefaultWorkspaceAsync();
        var dbPath = Path.Combine(_workspaceDir, "viewgrid.db");
        using var holder = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        var zipPath = CreateZipPath("with-open-db");
        var result = await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath);

        result.IsError.Should().BeFalse();
        using var zip = ZipFile.OpenRead(zipPath);
        zip.GetEntry("viewgrid.db").Should().NotBeNull();
    }

    [Fact]
    public async Task ImportAsync_RoundTrip_RestoresFiles()
    {
        await SeedDefaultWorkspaceAsync();
        var zipPath = CreateZipPath("roundtrip");
        (await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath)).IsError.Should().BeFalse();

        var result = await _manager.ImportAsync(zipPath, "imported", "インポート済み");

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("imported");
        result.Value.DisplayName.Should().Be("インポート済み");

        var importedDb = Path.Combine(_root.FullName, "workspaces", "imported", "viewgrid.db");
        File.Exists(importedDb).Should().BeTrue();
        (await File.ReadAllTextAsync(importedDb)).Should().Be("fake-db-content");

        var importedAsset = Path.Combine(_root.FullName, "workspaces", "imported", "assets", "ab", "abdef.png");
        File.Exists(importedAsset).Should().BeTrue();

        var entries = await _manager.ListAsync();
        entries.Should().Contain(m => m.Name == "imported" && m.DisplayName == "インポート済み");
    }

    [Fact]
    public async Task ImportAsync_RejectsConflictingName()
    {
        await SeedDefaultWorkspaceAsync();
        var zipPath = CreateZipPath("conflict");
        (await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath)).IsError.Should().BeFalse();
        (await _manager.CreateAsync("imported", "既存")).IsError.Should().BeFalse();

        var result = await _manager.ImportAsync(zipPath, "imported", "重複");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task ImportAsync_RejectsMissingMetadata()
    {
        // zip 内に workspace.json が無いケース (任意の zip)
        var zipPath = CreateZipPath("no-meta");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt", CompressionLevel.NoCompression);
        }

        var result = await _manager.ImportAsync(zipPath, "imported", "メタなし");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task ImportAsync_RejectsInvalidZip()
    {
        // zip ではない適当なファイル
        var zipPath = CreateZipPath("garbage");
        await File.WriteAllTextAsync(zipPath, "this is not a zip");

        var result = await _manager.ImportAsync(zipPath, "imported", "破損");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task ImportAsync_RejectsZipSlipPath()
    {
        // 親ディレクトリへ抜けるエントリ名を含む zip を作る
        var zipPath = CreateZipPath("zipslip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // メタデータは正常な内容で
            var meta = zip.CreateEntry("workspace.json", CompressionLevel.NoCompression);
            using (var s = meta.Open())
            using (var w = new StreamWriter(s, Encoding.UTF8))
            {
                w.Write("{\"name\":\"src\",\"displayName\":\"src\",\"exportedAt\":\"2026-05-07T00:00:00Z\"}");
            }
            // 危険なエントリ
            zip.CreateEntry("../../escaped.txt", CompressionLevel.NoCompression);
        }

        var result = await _manager.ImportAsync(zipPath, "imported", "攻撃 zip");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
        // 部分展開の掃除を確認
        Directory.Exists(Path.Combine(_root.FullName, "workspaces", "imported")).Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_RejectsAlternateDataStreamPath()
    {
        // NTFS ADS 構文を含むエントリ名は無条件で拒否する (Windows での別ファイル書き込み防止)。
        var zipPath = CreateZipPath("ads-attack");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var meta = zip.CreateEntry("workspace.json", CompressionLevel.NoCompression);
            using (var s = meta.Open())
            using (var w = new StreamWriter(s, Encoding.UTF8))
            {
                w.Write("{\"name\":\"src\",\"displayName\":\"src\",\"exportedAt\":\"2026-05-07T00:00:00Z\"}");
            }
            zip.CreateEntry("viewgrid.db:hidden", CompressionLevel.NoCompression);
        }

        var result = await _manager.ImportAsync(zipPath, "imported", "ADS 攻撃 zip");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
        Directory.Exists(Path.Combine(_root.FullName, "workspaces", "imported")).Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_RejectsMissingFile()
    {
        var result = await _manager.ImportAsync(
            Path.Combine(_root.FullName, "does-not-exist.zip"),
            "imported", "未存在");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task ImportAsync_RejectsInvalidName()
    {
        await SeedDefaultWorkspaceAsync();
        var zipPath = CreateZipPath("bad-name");
        (await _manager.ExportAsync(WorkspaceBootstrap.DefaultWorkspaceName, zipPath)).IsError.Should().BeFalse();

        var result = await _manager.ImportAsync(zipPath, "with space", "不正名");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }
}
