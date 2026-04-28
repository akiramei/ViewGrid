using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class AssetLibraryView : UserControl
{
    public AssetLibraryView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ListBox のマルチセレクト状態を VM の <see cref="AssetLibraryViewModel.SelectedAssets"/>
    /// コレクションに同期する。Avalonia の <c>SelectedItems</c> は VM への双方向バインドが
    /// 安定しないため、code-behind 経由で明示的にブリッジする。
    /// </summary>
    private void OnAssetListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AssetLibraryViewModel vm || sender is not ListBox listBox)
            return;

        // VM 側で Clear+Add の連鎖通知を 1 回にまとめてもらう（多重ロード防止）
        var items = listBox.SelectedItems?.OfType<AssetItemViewModel>().ToList() ?? new();
        vm.UpdateSelectedAssets(items);
    }
}
