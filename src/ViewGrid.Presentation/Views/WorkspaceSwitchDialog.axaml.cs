using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    private void OnBeginCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSwitchDialogViewModel vm)
            vm.BeginCreate();
    }

    private void OnDuplicateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSwitchDialogViewModel vm)
            vm.BeginDuplicate();
    }

    private void OnCancelCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSwitchDialogViewModel vm)
            vm.CancelCreate();
    }

    private async void OnConfirmCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSwitchDialogViewModel vm)
            await vm.ConfirmCreateAsync();
    }

    private async void OnRenameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSwitchDialogViewModel vm)
            await vm.RenameSelectedAsync();
    }

    /// <summary>
    /// 「エクスポート...」: 選択中ワークスペースを zip にまとめて書き出す。
    /// SaveFilePicker で保存先を尋ね、 確定後に <see cref="WorkspaceSwitchDialogViewModel.ExportSelectedAsync"/>
    /// で実行。 大規模なワークスペースは時間がかかるため StatusMessage で進行を伝える。
    /// <c>async void</c> ハンドラのため、 想定外例外は握り潰してダイアログ上に表示する
    /// (未キャッチ例外はプロセス強制終了の原因になる)。
    /// </summary>
    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceSwitchDialogViewModel vm) return;
        if (vm.SelectedWorkspace is not { } sel) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "ワークスペースをエクスポート",
                DefaultExtension = "zip",
                SuggestedFileName = $"{sel.Name}.zip",
                FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            });
            if (file is null) return;

            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            await vm.ExportSelectedAsync(path);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"エクスポートに失敗しました: {ex.Message}";
        }
    }

    /// <summary>
    /// 「インポート...」: zip ファイルを選んで <see cref="WorkspaceSwitchDialogViewModel.BeginImportAsync"/>
    /// に渡し、 既存の Create カードに切替えてユーザーに名前 / 表示名を確認させる。
    /// 実際の展開は <see cref="WorkspaceSwitchDialogViewModel.ConfirmCreateAsync"/> で行う。
    /// </summary>
    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceSwitchDialogViewModel vm) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "インポートするワークスペース zip を選択",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            });
            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            await vm.BeginImportAsync(path);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"インポートの準備に失敗しました: {ex.Message}";
        }
    }

    /// <summary>
    /// 「このワークスペースを削除」: 確認ダイアログを挟んで <see cref="WorkspaceSwitchDialogViewModel.DeleteSelectedAsync"/>。
    /// 削除と言っても workspaces/.trash/ への Move なので、 ファイル自体は残る。
    /// </summary>
    private async void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceSwitchDialogViewModel vm) return;
        if (vm.SelectedWorkspace is not { } sel) return;

        var confirmed = await ConfirmDialog.ShowAsync(
            this,
            "ワークスペースを削除",
            $"「{sel.DisplayName}」を削除しますか?\nworkspaces/.trash/ にバックアップが残るため、 必要なら手動で復元できます。",
            confirmLabel: "削除");
        if (!confirmed) return;

        await vm.DeleteSelectedAsync();
    }

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
