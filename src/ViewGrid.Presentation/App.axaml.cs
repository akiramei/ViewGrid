using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            // 設定からテーマを適用 + 設定変更時の即時切替を購読
            var settings = _services.GetRequiredService<IAppSettingsService>();
            ApplyTheme(settings.Current);
            settings.Changed += (_, s) => ApplyTheme(s);

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
}
