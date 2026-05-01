using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class CopyListView : UserControl
{
    public CopyListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ListBox のマルチセレクト状態を VM の <see cref="CopyListViewModel.SelectedCopies"/>
    /// コレクションに同期する。
    /// </summary>
    private void OnCopyListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not CopyListViewModel vm || sender is not ListBox listBox)
            return;

        var items = listBox.SelectedItems?.OfType<CopyItemViewModel>().ToList() ?? new();
        vm.UpdateSelectedCopies(items);
    }

    /// <summary>
    /// ダブルクリックでインラインリネーム編集を開始する。クリック位置から
    /// 該当 ListBoxItem を解決し、その DataContext (<see cref="CopyItemViewModel"/>) に対して
    /// <see cref="CopyListViewModel.BeginEdit"/> を呼ぶ。
    /// </summary>
    private void OnCopyListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not CopyListViewModel vm) return;
        if (e.Source is not Control src) return;

        // ListBoxItem まで遡って DataContext を取得（DataTemplate 内の子要素から発火するため）
        var item = src as ListBoxItem ?? src.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is not CopyItemViewModel copy) return;

        vm.BeginEdit(copy);
        e.Handled = true;
    }

    /// <summary>
    /// F2 キーで選択中のコピーをリネーム編集モードに切り替える。
    /// </summary>
    private void OnCopyListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2) return;
        if (DataContext is not CopyListViewModel vm) return;
        if (vm.SelectedCopy is not { } copy) return;

        vm.BeginEdit(copy);
        e.Handled = true;
    }

    /// <summary>
    /// 編集 TextBox が visual tree に attach されたタイミングでフォーカス + 全選択する
    /// （IsVisible=true で生成された直後）。
    /// </summary>
    private void OnEditingTextBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox tb) return;
        // Focus + SelectAll で「すぐ上書き入力できる」状態にする
        tb.Focus();
        tb.SelectAll();
    }

    /// <summary>
    /// 編集 TextBox 上の Enter で確定 / Esc でキャンセル。
    /// </summary>
    private async void OnEditingTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not CopyItemViewModel item) return;
        if (DataContext is not CopyListViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await vm.CommitEditAsync(item);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEdit(item);
        }
    }

    /// <summary>
    /// フォーカス喪失で確定（一般的なインラインリネーム UX）。Esc キャンセルが
    /// <see cref="OnEditingTextBoxKeyDown"/> で先に IsEditing=false にしている場合は no-op。
    /// </summary>
    private async void OnEditingTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not CopyItemViewModel item) return;
        if (DataContext is not CopyListViewModel vm) return;
        if (!item.IsEditing) return; // 既にキャンセル / 確定済み

        await vm.CommitEditAsync(item);
    }
}
