using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;
using ViewGrid.Presentation.Services;

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
            // 設定からテーマ + アクセント色を適用 + 設定変更時の即時切替を購読
            var settings = _services.GetRequiredService<IAppSettingsService>();
            ApplyTheme(settings.Current);
            ApplyAccentColor(settings.Current);
            settings.Changed += (_, s) =>
            {
                ApplyTheme(s);
                ApplyAccentColor(s);
            };

            var vm = _services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };

            // FilePickerService は MainWindow を owner として使うので、ここで注入する
            _services.GetRequiredService<AvaloniaFilePickerService>().SetOwnerWindow(window);

            desktop.MainWindow = window;

            // 初回起動時にアセット一覧とグリッド一覧を読み込み
            _ = vm.AssetLibrary.LoadAsync();
            _ = vm.GridList.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
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
    /// テーマ (Light / Dark) によって base 色を切り替える必要があるため、 現在の
    /// <see cref="Avalonia.Application.ActualThemeVariant"/> を見てプリセット内の
    /// Light / Dark パレットのどちらを使うかを判定する。 起動時 + 設定変更時 + テーマ切替時
    /// に呼ばれる。
    /// </para>
    /// </summary>
    private void ApplyAccentColor(AppSettings settings)
    {
        var preset = AccentColorPresets.Get(settings.AccentColor);
        var palette = ActualThemeVariant == ThemeVariant.Dark ? preset.Dark : preset.Light;

        Resources["SystemAccentColor"] = Color.Parse(palette.Color);
        Resources["SystemAccentColorDark1"] = Color.Parse(palette.Dark1);
        Resources["SystemAccentColorDark2"] = Color.Parse(palette.Dark2);
        Resources["SystemAccentColorDark3"] = Color.Parse(palette.Dark3);
        Resources["SystemAccentColorLight1"] = Color.Parse(palette.Light1);
        Resources["SystemAccentColorLight2"] = Color.Parse(palette.Light2);
        Resources["SystemAccentColorLight3"] = Color.Parse(palette.Light3);
    }
}
