using System.Globalization;
using System.Text;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ViewGrid.Application;
using ViewGrid.Infrastructure;
using ViewGrid.Infrastructure.Services;
using ViewGrid.Presentation.Services;

namespace ViewGrid.Presentation;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Windows コンソールの既定 OutputEncoding は cp932（Shift-JIS 系）で、
        // Serilog Console sink が UTF-8 で書き出すログが文字化けする。明示的に UTF-8 にする
        // ことで「アセット」「バリアント」等の日本語ログが正しく表示される。
        // ファイルログ (viewgrid-*.log) は元々 UTF-8 なので影響なし。
        // try/catch: コンソールがリダイレクトされた環境（ファイル / パイプ）では
        // SetEncoding が Unsupported になることがあるため、失敗しても無視する。
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (IOException) { }

        var (host, heldLock) = BuildHost(args);

        try
        {
            host.Start();

            // ロック取得成功時のみ DB マイグレーションを適用 (失敗時は同 DB を別プロセスが
            // 触っている可能性があるため、 こちらから書き込みに行かない)。
            if (heldLock is not null)
            {
                host.Services.ApplyMigrationsAsync().GetAwaiter().GetResult();
            }

            BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // ロック解放は host を完全に停止 / 破棄してから。 DI に登録された DbContext や
            // 各種 IDisposable サービスが SQLite を閉じる前にロックを開放してしまうと、
            // 別プロセスが起動して同時アクセスを始める競合窓ができる。
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
            heldLock?.Dispose();
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    // デザイナ実行時 (Avalonia preview / xaml editor) はパラメータなし App() で起動する
    // ため、 ここで Host を構築しない。 BuildHost を呼ぶと WorkspaceBootstrap や
    // WorkspaceLock.TryAcquire が走って LocalAppData を触る副作用が出るため避ける。
    // 実行時 Main からは下の <c>BuildAvaloniaApp(IServiceProvider)</c> overload が使われる。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Host を構築する。 副作用としてワークスペース解決 + 旧バージョン自動移行 +
    /// ファイルロック取得を試みる。 ロック取得に失敗した場合は <see cref="App"/> 側で
    /// メインウィンドウの代わりに <see cref="Views.WorkspaceLockedDialog"/> を表示する。
    /// </summary>
    private static (IHost Host, WorkspaceLock? HeldLock) BuildHost(string[] args)
    {
        var rootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViewGrid");
        Directory.CreateDirectory(rootDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                path: Path.Combine(rootDir, "logs", "viewgrid-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        // ワークスペース解決 (旧バージョンからの自動移行 + active.json 解決)。
        // --workspace=<name> 引数 (再起動経路) があれば優先する。
        var cliWorkspace = ParseWorkspaceArg(args);
        var (activeWorkspace, workspaceDir) = WorkspaceBootstrap.Resolve(rootDir, cliWorkspace);

        // 同一ワークスペースの二重起動を阻止するファイルロック。 失敗時は App 側で
        // 警告ダイアログを出して通常起動はしない。
        var lockResult = WorkspaceLock.TryAcquire(workspaceDir);
        var lockedState = new WorkspaceLockedState(
            IsLocked: !lockResult.Acquired,
            ActiveWorkspaceName: activeWorkspace,
            OwnerProcessId: lockResult.OwnerProcessId);

        var host = Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(lockedState);
                services
                    .AddInfrastructure(rootDir, workspaceDir, activeWorkspace)
                    .AddApplication()
                    .AddPresentation();
            })
            .Build();

        return (host, lockResult.Lock);
    }

    /// <summary>
    /// <c>--workspace=&lt;name&gt;</c> をコマンドライン引数から抽出する (再起動経路で渡される)。
    /// 見つからなければ <c>null</c>。
    /// </summary>
    private static string? ParseWorkspaceArg(string[] args)
    {
        const string prefix = "--workspace=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg[prefix.Length..];
        }
        return null;
    }
}
