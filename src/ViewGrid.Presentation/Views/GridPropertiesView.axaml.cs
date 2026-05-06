using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class GridPropertiesView : UserControl
{
    public GridPropertiesView() => InitializeComponent();

    /// <summary>
    /// Enter で確定 / Esc で revert。 確定は <see cref="CommitNameAsync"/> 経由で
    /// <see cref="GridCanvasListViewModel.RenameSelectedAsync"/> を呼び、 履歴に積む
    /// (空欄 / 同名は no-op)。 revert は VM.Name で TextBox.Text を上書き。
    /// </summary>
    private async void OnGridNameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await CommitNameAsync(tb);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (DataContext is GridCanvasItemViewModel item)
                tb.Text = item.Name;
        }
    }

    /// <summary>
    /// フォーカス喪失で確定 (一般的なインライン編集 UX)。 Esc キャンセル後の LostFocus も
    /// <see cref="CommitNameAsync"/> 内で「Text == Name」判定で no-op になる。
    /// </summary>
    private async void OnGridNameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) await CommitNameAsync(tb);
    }

    /// <summary>
    /// TextBox.Text を trim し、 空欄 or VM.Name と同じなら revert (TextBox.Text = VM.Name)、
    /// 違えば <see cref="GridCanvasListViewModel.RenameSelectedAsync"/> を呼ぶ。
    /// VM 側で履歴に積まれて Name が更新されたら OneWay バインドで TextBox.Text に反映される。
    /// </summary>
    private async Task CommitNameAsync(TextBox tb)
    {
        if (DataContext is not GridCanvasItemViewModel item) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (owner.DataContext is not MainWindowViewModel mainVm) return;

        var newName = tb.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(newName) || newName == item.Name)
        {
            tb.Text = item.Name;
            return;
        }
        // SelectedGrid と DataContext がズレた状況 (再ロード中) では誤対象に rename しないためガード。
        if (mainVm.GridList.SelectedGrid?.GridId != item.GridId) return;

        await mainVm.GridList.RenameSelectedAsync(newName);
    }
}
