using System.Collections.Immutable;
using System.Globalization;
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
}
