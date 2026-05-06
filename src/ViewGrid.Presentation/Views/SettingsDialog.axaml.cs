using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Settings;

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

    /// <summary>
    /// アクセント色ドット (Border) クリック: VM の AccentColor を更新する。
    /// Button だと FluentTheme の :pointerover スタイルが Background をグレーに上書きして
    /// Swatch 色が見えなくなるので Border + PointerPressed で代替。
    /// Border.Tag に <see cref="AccentColorPreset.Id"/> を入れているので取り出して反映。
    /// VM のセッターで JSON 保存 + Application.Resources 更新までトリガされる。
    /// </summary>
    private void OnAccentPresetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not string id) return;
        if (DataContext is not SettingsDialogViewModel vm) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
        vm.AccentColor = id;
    }
}
