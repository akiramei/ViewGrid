using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ViewGrid.Application.History;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0)
            return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();

        if (paths.Count > 0)
            await vm.AssetLibrary.AddFilesAsync(paths);
    }

    /// <summary>
    /// 履歴 Flyout 内の ListBox 項目クリック処理。<see cref="MainWindowViewModel.JumpToHistoryAsync"/>
    /// を発火し、Flyout を自動で閉じる。
    /// <para>
    /// <c>SelectionChanged</c> ではなく <c>PointerReleased</c> を起点にする理由:
    /// SelectionChanged は <c>SelectedIndex={Binding CurrentHistoryIndex}</c> の OneWay バインドや
    /// 初期表示時の自動選択でも発火するため、JumpTo の意図が混入する。マウス左ボタンの
    /// PointerReleased ならユーザー操作起点に絞れる。
    /// </para>
    /// </summary>
    private void OnHistoryItemClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (sender is not ListBox lb)
            return;
        if (lb.SelectedItem is not HistoryEntry entry)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        // ジャンプは fire-and-forget。完了は StateChanged → ListBox 再描画で反映。
        _ = vm.JumpToHistoryAsync(entry.Index);

        // Flyout を閉じる。Avalonia 12 では Flyout 自体に x:Name 経由のフィールドが
        // 生成されないため、ホストの Button 経由で Flyout プロパティにアクセスする。
        if (HistoryDropdownButton.Flyout is Flyout flyout)
            flyout.Hide();

        // 次回開いた時に「選択された見た目」が残らないよう、選択をクリア
        lb.SelectedItem = null;
    }
}
