using System.Text.Json;
using ViewGrid.Core.Settings;

namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// 起動時 (DI 構築前) に呼ばれるワークスペース解決ロジック。
/// active.json の読み書き、 旧バージョンからのデータ自動移行、 ワークスペースディレクトリの作成を行う。
/// </summary>
public static class WorkspaceBootstrap
{
    /// <summary>既定ワークスペース名 (旧バージョンからの移行先 / active.json 不在時のフォールバック)。</summary>
    public const string DefaultWorkspaceName = "Default";

    /// <summary>ワークスペース親ディレクトリ名 (RootDirectory 直下)。</summary>
    public const string WorkspacesSubdirectory = "workspaces";

    private const string ActiveFileName = "active.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 起動時のワークスペース解決。 副作用として:
    /// <list type="bullet">
    /// <item>旧 <c>{root}/viewgrid.db</c> 等を <c>{root}/workspaces/Default/</c> に移行 (idempotent)</item>
    /// <item><c>{root}/active.json</c> が無ければ <see cref="DefaultWorkspaceName"/> で書き出す</item>
    /// <item>選ばれたワークスペースのディレクトリを作成</item>
    /// </list>
    /// </summary>
    /// <param name="rootDirectory">データルート (例: <c>%LocalAppData%\ViewGrid</c>)。 既存のフォルダを想定。</param>
    /// <param name="cliWorkspaceName">
    /// コマンドライン <c>--workspace=&lt;name&gt;</c> で指定された名前 (再起動経路で渡る)。
    /// 指定があれば <c>active.json</c> より優先し、 active.json も書き換える。
    /// </param>
    /// <returns>解決済みのワークスペース名と絶対パス。</returns>
    public static (string ActiveName, string WorkspaceDirectory) Resolve(string rootDirectory, string? cliWorkspaceName)
    {
        var resolution = ResolveWithRecovery(rootDirectory, cliWorkspaceName);
        return (resolution.ActiveName, resolution.WorkspaceDirectory);
    }

    /// <summary>
    /// <see cref="Resolve"/> に加えて、 選ばれたワークスペースのデータが見つからなかった (移動・削除された) ことを
    /// 呼び出し側へ伝える。 見つからないまま同名の空フォルダを黙って作り直すと、 空のワークスペースが
    /// 元の案件に見えてしまう。 そこで次の順に解決し、 欠落を <see cref="WorkspaceResolution.MissingWorkspaceName"/> で通知する:
    /// <list type="number">
    /// <item><c>workspaces.json</c> の先頭にある、 ディレクトリが実在する別のワークスペースを開く (active.json も更新)</item>
    /// <item>実在する別のワークスペースが無ければ、 要求された名前で空のワークスペースを作る</item>
    /// </list>
    /// 初回起動 (どのワークスペースも未登録) は欠落ではなく、 通知しない。
    /// </summary>
    public static WorkspaceResolution ResolveWithRecovery(string rootDirectory, string? cliWorkspaceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);

        MigrateLegacyDataIfNeeded(rootDirectory);

        var requested = ResolveActiveName(rootDirectory, cliWorkspaceName);
        var workspacesRoot = Path.Combine(rootDirectory, WorkspacesSubdirectory);
        var activeName = requested;
        var workspaceDir = Path.Combine(workspacesRoot, requested);
        string? missing = null;

        if (!Directory.Exists(workspaceDir))
        {
            var known = ReadManifestNames(rootDirectory);
            var fallback = known.FirstOrDefault(n =>
                !string.Equals(n, requested, StringComparison.OrdinalIgnoreCase)
                && IsValidName(n)
                && Directory.Exists(Path.Combine(workspacesRoot, n)));

            if (fallback is not null)
            {
                missing = requested;
                activeName = fallback;
                workspaceDir = Path.Combine(workspacesRoot, fallback);
                WriteActive(rootDirectory, activeName);
            }
            else if (known.Any(n => string.Equals(n, requested, StringComparison.OrdinalIgnoreCase)))
            {
                // 登録済みなのにデータが無く、 代わりに開けるものも無い。 空で作るが、 呼び出し側が通知する。
                missing = requested;
            }
        }

        Directory.CreateDirectory(workspaceDir);
        return new WorkspaceResolution(activeName, workspaceDir, missing);
    }

    /// <summary><c>workspaces.json</c> に登録されているワークスペース名 (登録順)。 無い・壊れている場合は空。</summary>
    private static List<string> ReadManifestNames(string rootDirectory)
    {
        var path = Path.Combine(rootDirectory, "workspaces.json");
        if (!File.Exists(path)) return [];
        try
        {
            var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path));
            return entries?.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.Name!).ToList() ?? [];
        }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
    }

    private sealed class ManifestEntry
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// 旧バージョン (<c>{root}/viewgrid.db</c> を直置き) からの自動移行。
    /// <c>{root}/workspaces/</c> が既に存在する (= 移行済み or 新規ユーザーで未作成) 場合は何もしない。
    /// 旧 DB が存在 + workspaces 未作成のときのみ、 <c>{root}/workspaces/Default/</c> に
    /// <c>viewgrid.db</c> / <c>assets/</c> / <c>thumbnails/</c> を移動する。
    /// </summary>
    private static void MigrateLegacyDataIfNeeded(string rootDirectory)
    {
        var workspacesDir = Path.Combine(rootDirectory, WorkspacesSubdirectory);
        if (Directory.Exists(workspacesDir))
            return;

        var legacyDb = Path.Combine(rootDirectory, "viewgrid.db");
        var legacyAssets = Path.Combine(rootDirectory, "assets");
        var legacyThumbnails = Path.Combine(rootDirectory, "thumbnails");

        var hasLegacyData = File.Exists(legacyDb)
            || Directory.Exists(legacyAssets)
            || Directory.Exists(legacyThumbnails);
        if (!hasLegacyData)
            return;

        var defaultDir = Path.Combine(workspacesDir, DefaultWorkspaceName);
        Directory.CreateDirectory(defaultDir);

        if (File.Exists(legacyDb))
            File.Move(legacyDb, Path.Combine(defaultDir, "viewgrid.db"));
        if (Directory.Exists(legacyAssets))
            Directory.Move(legacyAssets, Path.Combine(defaultDir, "assets"));
        if (Directory.Exists(legacyThumbnails))
            Directory.Move(legacyThumbnails, Path.Combine(defaultDir, "thumbnails"));
    }

    private static string ResolveActiveName(string rootDirectory, string? cliWorkspaceName)
    {
        // CLI 引数が最優先。 active.json も書き換えて整合させる。
        if (!string.IsNullOrWhiteSpace(cliWorkspaceName) && IsValidName(cliWorkspaceName))
        {
            WriteActive(rootDirectory, cliWorkspaceName);
            return cliWorkspaceName;
        }

        var activePath = Path.Combine(rootDirectory, ActiveFileName);
        if (File.Exists(activePath))
        {
            try
            {
                var json = File.ReadAllText(activePath);
                var doc = JsonSerializer.Deserialize<ActiveJson>(json);
                if (doc is { Name: { } stored } && IsValidName(stored))
                    return stored;
            }
            catch (JsonException)
            {
                // 破損は default にフォールバック
            }
            catch (IOException)
            {
                // 読込失敗も default にフォールバック
            }
        }

        WriteActive(rootDirectory, DefaultWorkspaceName);
        return DefaultWorkspaceName;
    }

    private static void WriteActive(string rootDirectory, string workspaceName)
    {
        var activePath = Path.Combine(rootDirectory, ActiveFileName);
        var json = JsonSerializer.Serialize(new ActiveJson { Name = workspaceName }, JsonOptions);
        File.WriteAllText(activePath, json);
    }

    /// <summary>
    /// ワークスペース名の制約チェック。 FS 互換のため英数 + ハイフン + アンダースコアのみ許容。
    /// 表示名 (日本語可) は別フィールド (<see cref="WorkspaceManifest.DisplayName"/>) で扱う。
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.Length > 64)
            return false;
        foreach (var ch in name)
        {
            var ok = (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '-' || ch == '_';
            if (!ok) return false;
        }
        return true;
    }

    private sealed class ActiveJson
    {
        public string? Name { get; set; }
    }
}

/// <summary>
/// 起動時のワークスペース解決結果。
/// </summary>
/// <param name="ActiveName">開くワークスペース名。</param>
/// <param name="WorkspaceDirectory">そのデータディレクトリ (作成済み)。</param>
/// <param name="MissingWorkspaceName">
/// 要求されたワークスペースのデータが見つからなかった場合のその名前。 <see cref="ActiveName"/> と違えば別の
/// ワークスペースへ切り替えて開いた、 同じなら空のワークスペースとして作り直した。 欠落なしなら <c>null</c>。
/// </param>
public sealed record WorkspaceResolution(string ActiveName, string WorkspaceDirectory, string? MissingWorkspaceName);
