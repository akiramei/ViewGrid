using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewGrid.Core.Services;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// 未保存の編集の扱いを「保存 / 破棄 / 戻る」 で尋ねるダイアログ。 静的ヘルパ <see cref="ShowAsync"/> 経由で使う。
/// ウィンドウの×や Esc で閉じた場合は <see cref="UnsavedChoice.Cancel"/> (編集を失わない側)。
/// </summary>
public partial class UnsavedChangesDialog : Window
{
    private UnsavedChoice _choice = UnsavedChoice.Cancel;

    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    public static async Task<UnsavedChoice> ShowAsync(
        Window owner, string title, string message,
        string saveLabel, string discardLabel, string cancelLabel)
    {
        var dialog = new UnsavedChangesDialog { Title = title };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.SaveButton.Content = saveLabel;
        dialog.DiscardButton.Content = discardLabel;
        dialog.CancelButton.Content = cancelLabel;
        await dialog.ShowDialog(owner);
        return dialog._choice;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        _choice = UnsavedChoice.Save;
        Close();
    }

    private void OnDiscardClicked(object? sender, RoutedEventArgs e)
    {
        _choice = UnsavedChoice.Discard;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        _choice = UnsavedChoice.Cancel;
        Close();
    }
}
