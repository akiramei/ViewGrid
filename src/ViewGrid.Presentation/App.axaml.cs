using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.Localization;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;
using ViewGrid.Presentation.Localization;
using ViewGrid.Presentation.Services;
using ViewGrid.Presentation.Views;

namespace ViewGrid.Presentation;

public partial class App : global::Avalonia.Application
{
    private readonly IServiceProvider? _services;

    public App() { }

    public App(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// MainWindow 等の View からダイアログ用 VM を取得するためのアクセサ。
    /// `((App)Application.Current!).Services` で取り出す想定。 デザイン時 (`new App()`) は null。
    /// </summary>
    public IServiceProvider? Services => _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            // ItemViewModel 等の non-DI 経路から ILocalizationService を引けるように Singleton 注入。
            // 通常は DI コンストラクタ注入を使うが、 大量に new される ListBox 要素 VM のみここを経由する。
            LocAccessor.Current = _services.GetRequiredService<ILocalizationService>();

            // 設定からテーマ + アクセント色 + 言語を適用 + 設定変更時の即時切替を購読
            var settings = _services.GetRequiredService<IAppSettingsService>();
            ApplyTheme(settings.Current);
            ApplyAccentColor(settings.Current);
            ApplyLanguage(settings.Current);
            settings.Changed += (_, s) =>
            {
                ApplyTheme(s);
                ApplyAccentColor(s);
                ApplyLanguage(s);
            };

            // ワークスペースロック取得失敗時は MainWindow ではなく WorkspaceLockedDialog を
            // メインウィンドウとして表示する。 ユーザーが閉じるとデフォルトの ShutdownMode で
            // アプリ終了。 DI 経由で MainWindowViewModel を生成しないので、 DB に触れない。
            var lockedState = _services.GetRequiredService<WorkspaceLockedState>();
            if (lockedState.IsLocked)
            {
                var lockedDialog = new WorkspaceLockedDialog();
                lockedDialog.Configure(lockedState);
                desktop.MainWindow = lockedDialog;
            }
            else
            {
                var vm = _services.GetRequiredService<MainWindowViewModel>();
                var window = new MainWindow { DataContext = vm };

                // FilePickerService は MainWindow を owner として使うので、ここで注入する
                _services.GetRequiredService<AvaloniaFilePickerService>().SetOwnerWindow(window);

                // キャプチャモードではウィンドウサイズを固定し、スクリーンショットの寸法を一定にする。
                if (_services.GetRequiredService<CaptureModeState>().IsActive)
                {
                    window.Width = CaptureMode.WindowWidth;
                    window.Height = CaptureMode.WindowHeight;
                    window.CanResize = false;
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                desktop.MainWindow = window;

                // シャットダウン時に LastOpenedGridId の永続化が完了するまで待つ。
                // GridCanvasListViewModel / MainWindowViewModel は Transient 登録 (DependencyInjection.cs)
                // のため、 DI から取り直すと LastOpenedSaveTask が Task.CompletedTask な別インスタンスを
                // 引いてしまい race を取り逃がす。 必ず `desktop.MainWindow.DataContext` 経由で
                // live VM を辿って await する。
                desktop.ShutdownRequested += OnShutdownRequested;

                // 初回起動時にアセット一覧 / グリッド一覧 / 候補リストを読み込み。
                // 候補リスト (GridWorkspace.Candidates / CandidateGroups) は LoadGridAsync 経由
                // でしか populate されないが、 グリッドが 0 件のワークスペースだと SelectedGrid が
                // null のままで LoadGridAsync が走らない → 既存アセットがあるのに候補リストが空、
                // という UI 上「アセットが消えた」 ように見える状態が発生する。 起動時に明示的に
                // 呼ぶことで、 グリッド有無に依存せず DB の copy 一覧を candidate list に反映する。
                _ = vm.AssetLibrary.LoadAsync();
                _ = vm.GridList.LoadAsync();
                _ = vm.GridWorkspace.LoadCandidatesAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// シャットダウン要求の再入防止フラグ。 一度 await してから <c>desktop.Shutdown()</c> を再呼出しすると
    /// <see cref="IClassicDesktopStyleApplicationLifetime.ShutdownRequested"/> が再発火するので、
    /// 2 回目以降は素通しする。
    /// </summary>
    private bool _shutdownAwaited;

    /// <summary>
    /// MainWindowVM.Dispose の二重実行防止。 1 回目の await 後 + 2 回目の素通し時に
    /// 二度 Dispose を呼ばないようガード。
    /// </summary>
    private bool _mainVmDisposed;

    /// <summary>
    /// シャットダウン要求時に下記を待機する:
    /// <list type="bullet">
    ///   <item><see cref="GridCanvasListViewModel.LastOpenedSaveTask"/> = LastOpenedGridId の永続化</item>
    ///   <item><see cref="MainWindowViewModel.FlushAllAutoSavesAsync"/> = 保留中 auto-save の即実行</item>
    /// </list>
    /// 完了後に <see cref="MainWindowViewModel.Dispose"/> を 1 回だけ呼ぶ (Dispose 連鎖 + 購読解除)。
    /// MainWindow の DataContext から live VM を読む (DI Transient による race 回避、 設計上の罠)。
    /// 失敗 (権限不足 / Validation 等) は静かに飲んでシャットダウン進行を妨げない。
    /// </summary>
    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownAwaited) return;
        if (sender is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (desktop.MainWindow?.DataContext is not MainWindowViewModel mainVm) return;

        var pending = mainVm.GridList.LastOpenedSaveTask;
        var flushAll = mainVm.FlushAllAutoSavesAsync();

        if (pending.IsCompleted && flushAll.IsCompleted)
        {
            DisposeMainVmOnce(mainVm);
            return;
        }

        e.Cancel = true;
        _shutdownAwaited = true;
        try { await Task.WhenAll(pending, flushAll); }
        catch { /* 永続化失敗 / auto-save 失敗は致命的でないので無視 */ }
        DisposeMainVmOnce(mainVm);
        desktop.Shutdown();
    }

    private void DisposeMainVmOnce(MainWindowViewModel mainVm)
    {
        if (_mainVmDisposed) return;
        _mainVmDisposed = true;
        try { mainVm.Dispose(); }
        catch { /* Dispose 失敗で終了経路を妨げない */ }
    }

    /// <summary>
    /// <see cref="AppSettings.Theme"/> を <see cref="Avalonia.Application.RequestedThemeVariant"/>
    /// に反映する。 不明値は <see cref="ThemeVariant.Default"/> (システム追従) にフォールバック。
    /// </summary>
    private void ApplyTheme(AppSettings settings)
    {
        RequestedThemeVariant = settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>
    /// <see cref="AppSettings.AccentColor"/> プリセットの 7 色を <see cref="Application.Resources"/>
    /// (トップレベル) に書き込む。 ThemeDictionaries 内の同名キーよりも優先されるため、
    /// この経路で書くと <c>{DynamicResource}</c> 参照が確実に再評価される。
    /// <para>
    /// テーマ (Light / Dark) によって base 色を切り替える必要があるため、 まず
    /// <see cref="Avalonia.Application.RequestedThemeVariant"/> を見て Light / Dark を確定し
    /// (= 設定で明示選択時はここで決まる)、 Default (システム追従) のときのみ
    /// <see cref="Avalonia.Application.ActualThemeVariant"/> に問い合わせる。
    /// 旧実装は <c>ApplyTheme</c> 直後の <c>ActualThemeVariant</c> の更新遅延で前テーマの
    /// パレットが残る race があった。
    /// </para>
    /// </summary>
    private void ApplyAccentColor(AppSettings settings)
    {
        var preset = AccentColorPresets.Get(settings.AccentColor);
        var palette = ResolveIsDarkTheme() ? preset.Dark : preset.Light;

        Resources["SystemAccentColor"] = Color.Parse(palette.Color);
        Resources["SystemAccentColorDark1"] = Color.Parse(palette.Dark1);
        Resources["SystemAccentColorDark2"] = Color.Parse(palette.Dark2);
        Resources["SystemAccentColorDark3"] = Color.Parse(palette.Dark3);
        Resources["SystemAccentColorLight1"] = Color.Parse(palette.Light1);
        Resources["SystemAccentColorLight2"] = Color.Parse(palette.Light2);
        Resources["SystemAccentColorLight3"] = Color.Parse(palette.Light3);
    }

    /// <summary>
    /// 現在のテーマが Dark かどうかを判定する。 ユーザーが Light / Dark を明示選択している
    /// 場合は <see cref="Avalonia.Application.RequestedThemeVariant"/> で即座に確定するので
    /// <c>ApplyTheme</c> 直後でも race にならない。 Default (システム追従) の場合のみ
    /// <see cref="Avalonia.Application.ActualThemeVariant"/> を見る (こちらは OS 確定値で起動時から安定)。
    /// </summary>
    private bool ResolveIsDarkTheme()
    {
        if (RequestedThemeVariant == ThemeVariant.Light) return false;
        if (RequestedThemeVariant == ThemeVariant.Dark) return true;
        return ActualThemeVariant == ThemeVariant.Dark;
    }

    /// <summary>
    /// <see cref="AppSettings.Language"/> を <see cref="LocService"/> に流して全 i18n binding を再評価する。
    /// 値が "system" のときは <see cref="CultureInfo.CurrentUICulture"/> (OS ロケール) に従う。
    /// 不正値や未対応 culture は安全のため日本語 (ja) にフォールバック。
    /// </summary>
    private static void ApplyLanguage(AppSettings settings)
    {
        var culture = settings.Language switch
        {
            "ja" => new CultureInfo("ja"),
            "en" => new CultureInfo("en"),
            _ => CultureInfo.CurrentUICulture,
        };
        LocService.Instance.SetCulture(culture);
    }
}
