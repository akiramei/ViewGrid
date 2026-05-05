using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// 全サムネ再生成ダイアログ。 VM の <see cref="ThumbnailRegenDialogViewModel.CloseRequested"/>
/// イベント経由で Window を閉じる。 静的 <see cref="ShowAsync"/> ヘルパで起動する。
/// </summary>
public partial class ThumbnailRegenDialog : Window
{
    public ThumbnailRegenDialog()
    {
        InitializeComponent();
    }

    public static async System.Threading.Tasks.Task ShowAsync(Window owner, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(services);

        var vm = services.GetRequiredService<ThumbnailRegenDialogViewModel>();
        var dialog = new ThumbnailRegenDialog { DataContext = vm };
        // VM の CloseRequested で Window を閉じる
        vm.CloseRequested += (_, _) => dialog.Close();
        // ダイアログを閉じたら VM を Dispose して CTS / イベント購読を解放
        dialog.Closed += (_, _) => vm.Dispose();
        await dialog.ShowDialog(owner);
    }
}
