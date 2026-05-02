using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// 破壊的操作（削除等）の確認用シンプルダイアログ。タイトル + メッセージ + 確認/キャンセルボタンの 3 段構成。
/// 静的ヘルパ <see cref="ShowAsync"/> 経由で呼び出すことを想定。
/// </summary>
public partial class ConfirmDialog : Window
{
    private bool _confirmed;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async System.Threading.Tasks.Task<bool> ShowAsync(
        Window owner, string title, string message, string confirmLabel = "削除")
    {
        var dialog = new ConfirmDialog
        {
            Title = title,
        };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmLabel;
        await dialog.ShowDialog(owner);
        return dialog._confirmed;
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}
