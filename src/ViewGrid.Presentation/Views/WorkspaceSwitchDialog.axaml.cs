using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// ワークスペース切替ダイアログ。 一覧から選択し「再起動して切替」を押すと
/// <c>active.json</c> 書き換え + プロセス再起動 (<c>--workspace=&lt;name&gt;</c> 引数付き) で
/// 新ワークスペースのデータでアプリが再開する。
/// </summary>
public partial class WorkspaceSwitchDialog : Window
{
    public WorkspaceSwitchDialog()
    {
        InitializeComponent();
    }

    /// <summary>DI から VM を取得 + 一覧ロード後にダイアログを開く。</summary>
    public static async System.Threading.Tasks.Task ShowAsync(Window owner, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(services);

        var vm = services.GetRequiredService<WorkspaceSwitchDialogViewModel>();
        await vm.LoadAsync();
        var dialog = new WorkspaceSwitchDialog { DataContext = vm };
        await dialog.ShowDialog(owner);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 「再起動して切替」: SetActive で active.json を書き、 現 exe を <c>--workspace=&lt;name&gt;</c> 付きで
    /// 起動して、 自プロセスは <see cref="IClassicDesktopStyleApplicationLifetime.Shutdown(int)"/> で終了。
    /// </summary>
    private async void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceSwitchDialogViewModel vm) return;

        var newName = await vm.ApplyAsync();
        if (newName is null) return;

        if (!TryRestartWithWorkspace(newName))
        {
            // Process.Start に失敗してもダイアログは閉じない (StatusMessage で要因表示)
            return;
        }

        // 同プロセスは shutdown
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(0);
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// 現 exe を <c>--workspace=&lt;name&gt;</c> 引数付きで起動する。 起動 exe は
    /// <see cref="Environment.ProcessPath"/> から解決 (single-file publish も対応)。
    /// </summary>
    private bool TryRestartWithWorkspace(string workspaceName)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            if (DataContext is WorkspaceSwitchDialogViewModel vm)
                vm.StatusMessage = "実行中の exe パスを解決できないため再起動できません。";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
            };
            psi.ArgumentList.Add($"--workspace={workspaceName}");
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (DataContext is WorkspaceSwitchDialogViewModel vm)
                vm.StatusMessage = $"再起動に失敗しました: {ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            if (DataContext is WorkspaceSwitchDialogViewModel vm)
                vm.StatusMessage = $"再起動に失敗しました: {ex.Message}";
            return false;
        }
    }
}
