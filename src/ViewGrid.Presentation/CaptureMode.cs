using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Settings;

namespace ViewGrid.Presentation;

/// <summary>
/// キャプチャ用セットアップモードが有効かどうかを表す DI 登録値。
/// App 側がウィンドウサイズ固定などキャプチャ向け調整を行うために参照する。
/// </summary>
internal sealed record CaptureModeState(bool IsActive);

/// <summary>
/// マニュアル用スクリーンショット撮影を支援する「キャプチャモード」。
/// <c>--capture-mode</c> 付きで起動すると、ユーザーデータと隔離された一時ワークスペースに
/// サンプル画像を投入済みのクリーンな状態でアプリを開く。撮影者はそこから手動でグリッド作成・
/// 配置などを行いスクリーンショットを撮る (個別シーンの自動おぜん立てまでは行わない)。
/// </summary>
internal static class CaptureMode
{
    /// <summary>キャプチャモードを有効にする起動フラグ。</summary>
    public const string Flag = "--capture-mode";

    /// <summary>サンプル画像ディレクトリを明示指定する起動引数のプレフィックス。</summary>
    private const string SamplesArgPrefix = "--capture-samples=";

    /// <summary>撮影シナリオ ID を指定する起動引数のプレフィックス (例: <c>--capture-scenario=um-04-09-drop-valid</c>)。</summary>
    private const string ScenarioArgPrefix = "--capture-scenario=";

    /// <summary>キャプチャ用ワークスペース名 (一時ルート配下に作られる)。</summary>
    public const string WorkspaceName = "capture";

    /// <summary>
    /// キャプチャ時に固定するウィンドウ幅。 既存のマニュアル用スクリーンショット
    /// (docs/images/_raw/ の全画面キャプチャは 1442×932 = ウィンドウ枠込み) と寸法を揃えるため、
    /// MainWindow 既定の 1440×900 に固定する。 CAPTURE-LIST.md には 1280×800 と記載があるが
    /// 実際の撮影データは 1440×900 で、 ドキュメント側の記載が古い。
    /// </summary>
    public const int WindowWidth = 1440;

    /// <summary>キャプチャ時に固定するウィンドウ高さ (1440×900、 詳細は <see cref="WindowWidth"/> 参照)。</summary>
    public const int WindowHeight = 900;

    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// キャプチャモードが要求されているか。 <c>--capture-mode</c> 単独指定のほか、
    /// <c>--capture-scenario=&lt;id&gt;</c> の指定もキャプチャモードを含意する。
    /// </summary>
    public static bool IsRequested(string[] args) =>
        Array.Exists(args, a =>
            string.Equals(a, Flag, StringComparison.Ordinal)
            || a.StartsWith(ScenarioArgPrefix, StringComparison.Ordinal));

    /// <summary><c>--capture-scenario=&lt;id&gt;</c> の撮影シナリオ ID を取り出す。 無ければ null。</summary>
    public static string? ParseScenarioId(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(ScenarioArgPrefix, StringComparison.Ordinal))
                return arg[ScenarioArgPrefix.Length..];
        }
        return null;
    }

    /// <summary>
    /// ViewGrid 独自のキャプチャ用引数を取り除いた配列を返す。
    /// 値を持たない <c>--capture-mode</c> を Generic Host のコマンドライン設定や
    /// Avalonia に素通しすると解析エラーになりうるため、 起動前に除去する。
    /// </summary>
    public static string[] StripArgs(string[] args) =>
        Array.FindAll(args, a =>
            !string.Equals(a, Flag, StringComparison.Ordinal)
            && !a.StartsWith(SamplesArgPrefix, StringComparison.Ordinal)
            && !a.StartsWith(ScenarioArgPrefix, StringComparison.Ordinal));

    /// <summary>
    /// キャプチャ用の一時ルートディレクトリを用意する。
    /// 起動ごとに <c>%TEMP%\ViewGrid-capture\session-&lt;timestamp&gt;-&lt;rand&gt;</c> という
    /// 一意のサブディレクトリを新規作成する。
    /// <para>
    /// 既存ディレクトリを削除しない設計なので、 (1) 同時起動した別のキャプチャプロセスの
    /// ワークスペースを壊す危険がなく (削除がワークスペースロック取得より先に走る問題を回避)、
    /// (2) 毎回まっさらなディレクトリのためクリーンな状態が確実に保証される
    /// (削除失敗時に汚れたディレクトリを再利用する問題も発生しない)。
    /// </para>
    /// <para>
    /// 過去セッションのディレクトリは %TEMP% 配下に残るが、 OS の一時領域管理に委ねる
    /// (実行中セッションを壊さず安全に刈り取る判定は複雑なため、 あえて行わない)。
    /// </para>
    /// </summary>
    public static string PrepareRoot()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var unique = Guid.NewGuid().ToString("N")[..6];
        var root = Path.Combine(
            Path.GetTempPath(), "ViewGrid-capture", $"session-{stamp}-{unique}");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// キャプチャ用ルートに settings.json を書き出し、 テーマ Light / 言語 ja に固定する。
    /// CAPTURE-LIST.md の撮影方針 (Light テーマ・日本語) に合わせるため。
    /// </summary>
    public static void WriteCaptureSettings(string rootDir)
    {
        var settings = new AppSettings { Theme = "Light", Language = "ja" };
        var json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
        File.WriteAllText(Path.Combine(rootDir, "settings.json"), json);
    }

    /// <summary>
    /// マイグレーション後に <c>docs/sample-images/</c> の PNG をキャプチャ用ワークスペースへ取り込む。
    /// 取り込み元は <c>--capture-samples=&lt;dir&gt;</c> で明示指定でき、 未指定なら実行ファイルから
    /// 親方向に <c>docs/sample-images</c> を探索する (リポジトリ内から起動する想定)。
    /// </summary>
    public static async Task SeedSamplesAsync(IServiceProvider services, string[] args)
    {
        var samplesDir = ResolveSamplesDirectory(args);
        if (samplesDir is null)
        {
            Log.Warning("キャプチャモード: サンプル画像ディレクトリが見つからず、 シードをスキップしました。"
                + " --capture-samples=<dir> で明示指定できます。");
            return;
        }

        var files = Directory.GetFiles(samplesDir, "*.png");
        Array.Sort(files, StringComparer.Ordinal);

        using var scope = services.CreateScope();
        var import = scope.ServiceProvider.GetRequiredService<ImportImageUseCase>();

        var imported = 0;
        foreach (var file in files)
        {
            var result = await import.ExecuteAsync(
                new ImportImageRequest { SourcePath = file, SourceType = ImageSource.File });
            if (result.IsError)
            {
                Log.Warning("キャプチャモード: サンプル取り込み失敗 {File}: {Errors}",
                    Path.GetFileName(file),
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
            else
            {
                imported++;
            }
        }

        Log.Information("キャプチャモード: サンプル画像を {Imported}/{Total} 件取り込みました ({Dir})",
            imported, files.Length, samplesDir);
    }

    private static string? ParseSamplesArg(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(SamplesArgPrefix, StringComparison.Ordinal))
                return arg[SamplesArgPrefix.Length..];
        }
        return null;
    }

    /// <summary>
    /// 起動引数からサンプル画像ディレクトリを解決する (<c>--capture-samples</c> を優先)。
    /// シナリオ構築 (<see cref="CaptureScenarios"/>) からも利用する。
    /// </summary>
    internal static string? ResolveSamplesDirectory(string[] args) =>
        ResolveSamplesDirectory(ParseSamplesArg(args));

    /// <summary>
    /// サンプル画像ディレクトリを解決する。 明示指定があればそれを、 無ければ実行ファイルの
    /// 場所から親方向に <c>docs/sample-images</c> を探す。 見つからなければ null。
    /// </summary>
    private static string? ResolveSamplesDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return Directory.Exists(explicitDir) ? explicitDir : null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "sample-images");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
