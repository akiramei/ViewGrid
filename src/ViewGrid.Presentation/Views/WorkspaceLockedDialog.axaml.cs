using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewGrid.Presentation.Services;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// ワークスペースのファイルロック取得に失敗 (= 別プロセスが既に開いている) したときに
/// メインウィンドウの代わりに表示する通知ダイアログ。 「閉じる」 でアプリ終了。
/// </summary>
internal sealed partial class WorkspaceLockedDialog : Window
{
    public WorkspaceLockedDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 使用中ワークスペース名と (取得できれば) 占有プロセス PID を表示する。
    /// </summary>
    public void Configure(WorkspaceLockedState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        WorkspaceNameText.Text = $"ワークスペース: {state.ActiveWorkspaceName}";
        OwnerInfoText.Text = state.OwnerProcessId is { } pid
            ? $"プロセス ID {pid} が使用中です。"
            : "別のプロセスが使用中です。";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
