using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class GridPropertiesView : UserControl
{
    public GridPropertiesView()
    {
        InitializeComponent();
        // Esc で編集中の値を破棄して revert (CopyProperties / Inspector と同じ操作感)。
        // 子の TextBox / NumericUpDown でフォーカスがあっても Bubble で UserControl 直で受ける。
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (DataContext is not GridCanvasItemViewModel item || !item.IsDirty) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (owner.DataContext is not MainWindowViewModel mainVm) return;

        mainVm.GridList.RevertEditing();
        e.Handled = true;
    }

    /// <summary>
    /// 保存ボタン。 親 Window から MainWindowViewModel.GridList.CommitEditingAsync を呼ぶ。
    /// (UserControl 独自 NameScope を跨ぐので ElementName binding ではなく TopLevel 経由)
    /// </summary>
    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (owner.DataContext is not MainWindowViewModel mainVm) return;
        await mainVm.GridList.CommitEditingAsync();
    }

    /// <summary>リセットボタン。 ドラフトを永続化済み値に戻す (履歴は触らない)。</summary>
    private void OnRevertClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (owner.DataContext is not MainWindowViewModel mainVm) return;
        mainVm.GridList.RevertEditing();
    }
}
