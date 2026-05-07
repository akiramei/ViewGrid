using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ErrorOr;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// <see cref="IWorkspaceManager"/> のファイルシステム実装。
/// <c>workspaces.json</c> + <c>active.json</c> + <c>workspaces/&lt;name&gt;/</c> ディレクトリ群を操作する。
/// </summary>
internal sealed class FileSystemWorkspaceManager : IWorkspaceManager, IDisposable
{
    private const string ManifestFileName = "workspaces.json";
    private const string ActiveFileName = "active.json";
    private const string TrashDirectoryName = ".trash";

    /// <summary>エクスポート zip のルートに含めるメタデータファイル名。</summary>
    internal const string ExportMetadataFileName = "workspace.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IWorkspaceContext _context;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileSystemWorkspaceManager(IWorkspaceContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void Dispose() => _lock.Dispose();

    public async Task<IReadOnlyList<WorkspaceManifest>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return ReadManifestList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ErrorOr<WorkspaceManifest>> CreateAsync(
        string name, string displayName, CancellationToken ct = default)
    {
        if (!WorkspaceBootstrap.IsValidName(name))
            return Error.Validation("Workspace.InvalidName",
                "ワークスペース名は英数 / ハイフン / アンダースコアの 1〜64 文字で指定してください。");

        var trimmedDisplay = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedDisplay))
            return Error.Validation("Workspace.DisplayNameRequired", "表示名を入力してください。");

        await _lock.WaitAsync(ct);
        try
        {
            var manifests = ReadManifestList();
            if (manifests.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                return Error.Conflict("Workspace.NameAlreadyExists",
                    $"同名のワークスペース '{name}' が既に存在します。");

            var workspaceDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, name);
            if (Directory.Exists(workspaceDir))
                return Error.Conflict("Workspace.DirectoryAlreadyExists",
                    $"ディレクトリ '{workspaceDir}' が既に存在します。");

            Directory.CreateDirectory(workspaceDir);
            var entry = new WorkspaceManifest(name, trimmedDisplay);
            WriteManifestList(manifests.Append(entry));
            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ErrorOr<WorkspaceManifest>> DuplicateAsync(
        string sourceName, string newName, string newDisplayName, CancellationToken ct = default)
    {
        if (!WorkspaceBootstrap.IsValidName(newName))
            return Error.Validation("Workspace.InvalidName",
                "ワークスペース名は英数 / ハイフン / アンダースコアの 1〜64 文字で指定してください。");

        var trimmedDisplay = (newDisplayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedDisplay))
            return Error.Validation("Workspace.DisplayNameRequired", "表示名を入力してください。");

        await _lock.WaitAsync(ct);
        try
        {
            var sourceDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, sourceName);
            if (!Directory.Exists(sourceDir))
                return Error.NotFound("Workspace.NotFound",
                    $"複製元のワークスペース '{sourceName}' が見つかりません。");

            var manifests = ReadManifestList();
            if (manifests.Any(m => string.Equals(m.Name, newName, StringComparison.OrdinalIgnoreCase)))
                return Error.Conflict("Workspace.NameAlreadyExists",
                    $"同名のワークスペース '{newName}' が既に存在します。");

            var destDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, newName);
            if (Directory.Exists(destDir))
                return Error.Conflict("Workspace.DirectoryAlreadyExists",
                    $"ディレクトリ '{destDir}' が既に存在します。");

            // 失敗時は途中まで作ったディレクトリを掃除する。 SQLite WAL/SHM が残っていても
            // コピー後の DB は整合性が保たれる前提 (シングルユーザー / シングルプロセス設計)。
            try
            {
                await CopyDirectoryAsync(sourceDir, destDir, ct);
            }
            catch (Exception)
            {
                if (Directory.Exists(destDir))
                {
                    try { Directory.Delete(destDir, recursive: true); } catch (IOException) { }
                }
                throw;
            }

            var entry = new WorkspaceManifest(newName, trimmedDisplay);
            WriteManifestList(manifests.Append(entry));
            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// ディレクトリを再帰的にコピーする。 サブディレクトリ / ファイルを <paramref name="ct"/> で
    /// キャンセル可能。 大規模ワークスペース (画像 GB 級) でも時間はかかるが進行可能。
    /// </summary>
    private static async Task CopyDirectoryAsync(string source, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(dest, Path.GetFileName(dir));
            await CopyDirectoryAsync(dir, target, ct);
        }
    }

    public async Task<ErrorOr<Success>> RenameAsync(
        string name, string newDisplayName, CancellationToken ct = default)
    {
        var trimmed = (newDisplayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Error.Validation("Workspace.DisplayNameRequired", "表示名を入力してください。");

        await _lock.WaitAsync(ct);
        try
        {
            var manifests = ReadManifestList();
            var index = manifests.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return Error.NotFound("Workspace.NotFound",
                    $"ワークスペース '{name}' が見つかりません。");

            var updated = manifests.SetItem(index, manifests[index] with { DisplayName = trimmed });
            WriteManifestList(updated);
            return Result.Success;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ErrorOr<Success>> DeleteAsync(string name, CancellationToken ct = default)
    {
        if (!WorkspaceBootstrap.IsValidName(name))
            return Error.Validation("Workspace.InvalidName",
                "ワークスペース名の形式が不正です。");

        if (string.Equals(name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase))
            return Error.Conflict("Workspace.CannotDeleteActive",
                "現在開いているワークスペースは削除できません。 先に別のワークスペースに切替えてください。");

        await _lock.WaitAsync(ct);
        try
        {
            var manifests = ReadManifestList();
            var index = manifests.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

            var workspaceDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, name);
            if (index < 0 && !Directory.Exists(workspaceDir))
                return Error.NotFound("Workspace.NotFound",
                    $"ワークスペース '{name}' が見つかりません。");

            if (Directory.Exists(workspaceDir))
            {
                var trashRoot = Path.Combine(_context.RootDirectory,
                    WorkspaceBootstrap.WorkspacesSubdirectory, TrashDirectoryName);
                Directory.CreateDirectory(trashRoot);
                var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                var trashTarget = Path.Combine(trashRoot, $"{name}-{stamp}");
                Directory.Move(workspaceDir, trashTarget);
            }

            if (index >= 0)
                WriteManifestList(manifests.RemoveAt(index));

            return Result.Success;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ErrorOr<Success>> ExportAsync(
        string sourceName, string destinationZipPath, CancellationToken ct = default)
    {
        if (!WorkspaceBootstrap.IsValidName(sourceName))
            return Error.Validation("Workspace.InvalidName",
                "ワークスペース名の形式が不正です。");

        if (string.IsNullOrWhiteSpace(destinationZipPath))
            return Error.Validation("Workspace.InvalidExportPath",
                "エクスポート先のパスを指定してください。");

        await _lock.WaitAsync(ct);
        try
        {
            var sourceDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, sourceName);
            if (!Directory.Exists(sourceDir))
                return Error.NotFound("Workspace.NotFound",
                    $"エクスポート対象のワークスペース '{sourceName}' が見つかりません。");

            var manifests = ReadManifestList();
            var entry = manifests.FirstOrDefault(m =>
                string.Equals(m.Name, sourceName, StringComparison.OrdinalIgnoreCase));
            var displayName = entry?.DisplayName ?? sourceName;

            var sourceDirFull = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var destFull = Path.GetFullPath(destinationZipPath);
            if (destFull.StartsWith(sourceDirFull, StringComparison.OrdinalIgnoreCase))
                return Error.Validation("Workspace.ExportTargetInsideSource",
                    "エクスポート先がワークスペースディレクトリ内です。 ワークスペース外の場所を選んでください。");

            var parentDir = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            // 失敗時に半端な zip を残さないよう、 一時ファイル経由で書き出してから rename する。
            var tempZip = destinationZipPath + ".tmp";
            try
            {
                using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
                {
                    WriteExportMetadata(zip, sourceName, displayName);
                    AddWorkspaceFilesToZip(zip, sourceDir, ct);
                }

                // 既存 zip がある場合は File.Replace で原子的に置換する。 単純な「Delete → Move」 は
                // Delete 成功 + Move 失敗のタイミングで元ファイルを失うデータロス窓ができるため避ける。
                // 既存ファイルが無い場合は Replace は FileNotFoundException を出すので Move にフォールバック。
                if (File.Exists(destinationZipPath))
                    File.Replace(tempZip, destinationZipPath, destinationBackupFileName: null);
                else
                    File.Move(tempZip, destinationZipPath);
            }
            catch (IOException ex)
            {
                // SQLite が viewgrid.db を排他的に開いている場合や、 出力先がロックされている場合の
                // ファイル共有違反等。 アプリをクラッシュさせず StatusMessage に残すため Error 化する。
                try { if (File.Exists(tempZip)) File.Delete(tempZip); }
                catch (IOException) { }
                return Error.Conflict("Workspace.ExportFailed",
                    $"エクスポートに失敗しました: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); }
                catch (IOException) { }
                return Error.Conflict("Workspace.ExportFailed",
                    $"アクセス権限がないためエクスポートできません: {ex.Message}");
            }
            catch
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); }
                catch (IOException) { }
                throw;
            }

            return Result.Success;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// zip ルートに <c>workspace.json</c> (Name / DisplayName / ExportedAt) を書き出す。
    /// インポート時のデフォルト名候補に使われる。
    /// </summary>
    private static void WriteExportMetadata(ZipArchive zip, string name, string displayName)
    {
        var meta = new ExportMetadataJson
        {
            Name = name,
            DisplayName = displayName,
            ExportedAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var entry = zip.CreateEntry(ExportMetadataFileName, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(json);
    }

    /// <summary>
    /// ワークスペースディレクトリ配下のファイルを再帰的に zip に追加する。
    /// <see cref="WorkspaceLock.LockFileName"/> はランタイムで保持中のため除外。
    /// 万一ユーザーがワークスペースルートに <see cref="ExportMetadataFileName"/> を作っていた
    /// 場合も、 メタデータ書き込みとの重複を避けるため除外する。
    /// </summary>
    /// <remarks>
    /// <see cref="ZipArchive.CreateEntryFromFile(string, string, CompressionLevel)"/> は内部で
    /// <see cref="FileShare.Read"/> でファイルを開くため、 アクティブワークスペースの SQLite
    /// (<c>viewgrid.db</c> / <c>-wal</c> / <c>-shm</c>) を開けないことがある。 自前で
    /// <see cref="FileShare.ReadWrite"/> で開いてストリームコピーすることで、 アクティブな DB の
    /// エクスポートも成功させる。
    /// </remarks>
    private static void AddWorkspaceFilesToZip(ZipArchive zip, string sourceDir, CancellationToken ct)
    {
        var basePath = sourceDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        // ファイル一覧は zip 作成前のスナップショットを使う。 列挙中に zip 自身や WAL 更新で
        // ファイルが現れて無限再帰や自己参照に陥るのを防ぐ。
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, WorkspaceLock.LockFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(file, Path.Combine(sourceDir, ExportMetadataFileName), StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = file[basePath.Length..].Replace(Path.DirectorySeparatorChar, '/');
            var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
            try
            {
                // FileShare.ReadWrite | FileShare.Delete で開く: SQLite が viewgrid.db を保持して
                // いる状態でも読み取りに成功する (SQLite は WAL モードで Read|Write|Delete 共有で開く)。
                using var src = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var dst = entry.Open();
                src.CopyTo(dst);
            }
            catch (FileNotFoundException)
            {
                // 列挙中に消えた一時ファイル (SQLite -journal 等)。 スキップして続行。
            }
        }
    }

    public async Task<ErrorOr<WorkspaceManifest>> ImportAsync(
        string sourceZipPath, string newName, string newDisplayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceZipPath) || !File.Exists(sourceZipPath))
            return Error.NotFound("Workspace.ImportSourceMissing",
                "インポート元の zip ファイルが見つかりません。");

        if (!WorkspaceBootstrap.IsValidName(newName))
            return Error.Validation("Workspace.InvalidName",
                "ワークスペース名は英数 / ハイフン / アンダースコアの 1〜64 文字で指定してください。");

        var trimmedDisplay = (newDisplayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedDisplay))
            return Error.Validation("Workspace.DisplayNameRequired", "表示名を入力してください。");

        await _lock.WaitAsync(ct);
        try
        {
            var manifests = ReadManifestList();
            if (manifests.Any(m => string.Equals(m.Name, newName, StringComparison.OrdinalIgnoreCase)))
                return Error.Conflict("Workspace.NameAlreadyExists",
                    $"同名のワークスペース '{newName}' が既に存在します。");

            var destDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, newName);
            if (Directory.Exists(destDir))
                return Error.Conflict("Workspace.DirectoryAlreadyExists",
                    $"ディレクトリ '{destDir}' が既に存在します。");

            // zip を開いてメタデータと全エントリの安全性を先に検証する。 展開はすべての
            // エントリが安全 (zip slip 等の親ディレクトリ脱出が無い) と確認できてから一括で
            // 行う。 部分展開の掃除を 1 箇所に集約できる。
            try
            {
                using var zip = ZipFile.OpenRead(sourceZipPath);
                var metaEntry = zip.GetEntry(ExportMetadataFileName);
                if (metaEntry is null)
                    return Error.Validation("Workspace.InvalidExport",
                        "zip にワークスペースのメタデータ (workspace.json) が含まれていません。 ViewGrid からエクスポートした zip を指定してください。");

                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.Equals(entry.FullName, ExportMetadataFileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    if (ResolveSafeExtractPath(destDir, entry.FullName) is null)
                        return Error.Validation("Workspace.UnsafeZipEntry",
                            $"zip 内に親ディレクトリへ抜ける危険なパスが含まれています: '{entry.FullName}'");
                }

                Directory.CreateDirectory(destDir);
                try
                {
                    foreach (var entry in zip.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.Equals(entry.FullName, ExportMetadataFileName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        // 上で検証済みなので null 返却はあり得ない。
                        var destPath = ResolveSafeExtractPath(destDir, entry.FullName)!;
                        var entryDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(entryDir))
                            Directory.CreateDirectory(entryDir);
                        entry.ExtractToFile(destPath, overwrite: false);
                    }
                }
                catch
                {
                    // 部分展開の掃除
                    if (Directory.Exists(destDir))
                    {
                        try { Directory.Delete(destDir, recursive: true); } catch (IOException) { }
                    }
                    throw;
                }
            }
            catch (InvalidDataException)
            {
                return Error.Validation("Workspace.InvalidZip",
                    "zip ファイルが破損しているか、 形式が不正です。");
            }

            var added = new WorkspaceManifest(newName, trimmedDisplay);
            WriteManifestList(manifests.Append(added));
            return added;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<WorkspaceExportInfo?> PeekExportInfoAsync(string sourceZipPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceZipPath) || !File.Exists(sourceZipPath))
            return Task.FromResult<WorkspaceExportInfo?>(null);

        try
        {
            using var zip = ZipFile.OpenRead(sourceZipPath);
            var entry = zip.GetEntry(ExportMetadataFileName);
            if (entry is null) return Task.FromResult<WorkspaceExportInfo?>(null);

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            var meta = JsonSerializer.Deserialize<ExportMetadataJson>(json);
            if (string.IsNullOrEmpty(meta?.Name) || string.IsNullOrEmpty(meta.DisplayName))
                return Task.FromResult<WorkspaceExportInfo?>(null);

            return Task.FromResult<WorkspaceExportInfo?>(new WorkspaceExportInfo(meta.Name, meta.DisplayName));
        }
        catch (InvalidDataException) { return Task.FromResult<WorkspaceExportInfo?>(null); }
        catch (IOException) { return Task.FromResult<WorkspaceExportInfo?>(null); }
        catch (UnauthorizedAccessException) { return Task.FromResult<WorkspaceExportInfo?>(null); }
        catch (JsonException) { return Task.FromResult<WorkspaceExportInfo?>(null); }
    }

    /// <summary>
    /// zip エントリ名から実ファイルパスを解決する。 親ディレクトリへ抜ける zip slip 攻撃
    /// (<c>../../etc/passwd</c> 等) や NTFS の代替データストリーム (<c>foo:stream</c>) 経由の
    /// 脱出を弾くため、 解決後パスが <paramref name="destDir"/> 配下に留まることを検証する。
    /// 危険なエントリには <c>null</c> を返す。
    /// </summary>
    private static string? ResolveSafeExtractPath(string destDir, string entryFullName)
    {
        // NTFS ADS は <ファイル名>:<ストリーム名> 構文で別ファイルへ書き込めるため、 ':' を
        // 含むエントリ名は無条件で拒否する (Windows 専用脆弱性、 Linux でも安全側に倒す)。
        if (entryFullName.Contains(':', StringComparison.Ordinal))
            return null;

        var normalized = entryFullName.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(destDir, normalized));
        var fullDest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDest, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    public async Task<ErrorOr<Success>> SetActiveAsync(string name, CancellationToken ct = default)
    {
        if (!WorkspaceBootstrap.IsValidName(name))
            return Error.Validation("Workspace.InvalidName",
                "ワークスペース名の形式が不正です。");

        await _lock.WaitAsync(ct);
        try
        {
            var workspaceDir = Path.Combine(_context.RootDirectory,
                WorkspaceBootstrap.WorkspacesSubdirectory, name);
            if (!Directory.Exists(workspaceDir))
                return Error.NotFound("Workspace.NotFound",
                    $"ワークスペース '{name}' が見つかりません。");

            var activePath = Path.Combine(_context.RootDirectory, ActiveFileName);
            var json = JsonSerializer.Serialize(new ActiveJson { Name = name }, JsonOptions);
            await File.WriteAllTextAsync(activePath, json, ct);
            return Result.Success;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// <c>workspaces.json</c> を読み出す。 ファイル不在 / 破損時は active ワークスペース 1 件のみで初期化する。
    /// </summary>
    private ImmutableList<WorkspaceManifest> ReadManifestList()
    {
        var manifestPath = Path.Combine(_context.RootDirectory, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var entries = JsonSerializer.Deserialize<List<ManifestJson>>(json);
                if (entries is { Count: > 0 })
                {
                    var converted = entries
                        .Where(e => !string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.DisplayName))
                        .Select(e => new WorkspaceManifest(e.Name!, e.DisplayName!))
                        .ToImmutableList();
                    if (converted.Count > 0)
                        return EnsureContainsActive(converted);
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        // フォールバック: active 1 件だけ
        return [new WorkspaceManifest(_context.ActiveWorkspaceName, _context.ActiveWorkspaceName)];
    }

    /// <summary>
    /// 一覧に active ワークスペースが含まれていない場合 (破損 / 手動編集) は補う。
    /// </summary>
    private ImmutableList<WorkspaceManifest> EnsureContainsActive(ImmutableList<WorkspaceManifest> entries)
    {
        if (entries.Any(e => string.Equals(e.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase)))
            return entries;
        return entries.Insert(0, new WorkspaceManifest(_context.ActiveWorkspaceName, _context.ActiveWorkspaceName));
    }

    private void WriteManifestList(IEnumerable<WorkspaceManifest> entries)
    {
        var manifestPath = Path.Combine(_context.RootDirectory, ManifestFileName);
        var serializable = entries.Select(e => new ManifestJson { Name = e.Name, DisplayName = e.DisplayName }).ToList();
        var json = JsonSerializer.Serialize(serializable, JsonOptions);
        File.WriteAllText(manifestPath, json);
    }

    private sealed class ManifestJson
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class ActiveJson
    {
        public string? Name { get; set; }
    }

    private sealed class ExportMetadataJson
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? ExportedAt { get; set; }
    }
}
