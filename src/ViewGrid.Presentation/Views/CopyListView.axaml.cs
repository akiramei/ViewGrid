using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
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
}
