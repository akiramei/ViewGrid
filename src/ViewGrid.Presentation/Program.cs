using System;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using ViewGrid.Application;
using ViewGrid.Infrastructure;

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

        var host = BuildHost(args);
        host.Start();

        // 起動時にマイグレーションを適用
        host.Services.ApplyMigrationsAsync().GetAwaiter().GetResult();

        try
        {
            BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        BuildAvaloniaApp(BuildHost([]).Services);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static IHost BuildHost(string[] args)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViewGrid");
        Directory.CreateDirectory(dataDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                path: Path.Combine(dataDir, "logs", "viewgrid-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services
                    .AddInfrastructure(dataDir)
                    .AddApplication()
                    .AddPresentation();
            })
            .Build();
    }
}
