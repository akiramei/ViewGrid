using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.ViewModels;
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
}
