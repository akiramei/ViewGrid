using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// アプリ設定ダイアログ。 即時適用 + 自動保存型 (OK/Cancel ボタンなし、 「閉じる」 のみ)。
/// 静的ヘルパ <see cref="ShowAsync"/> 経由で MainWindow から起動する。
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// DI から VM を取得してダイアログを開く。 owner はダイアログを中央寄せする親 Window。
    /// </summary>
    public static async System.Threading.Tasks.Task ShowAsync(Window owner, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(services);

        var dialog = new SettingsDialog
        {
            DataContext = services.GetRequiredService<SettingsDialogViewModel>(),
        };
        await dialog.ShowDialog(owner);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 「全サムネを再生成...」 ボタン: <see cref="ThumbnailRegenDialog"/> をモーダル表示する。
    /// このダイアログを閉じずに上に重ねるので、 戻った後も設定ダイアログは継続。
    /// </summary>
    private async void OnRegenerateThumbnailsClicked(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current is not App app || app.Services is null) return;
        await ThumbnailRegenDialog.ShowAsync(this, app.Services);
    }
}
