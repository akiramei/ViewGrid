using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ViewGrid.Application.History;
using ViewGrid.Application.ViewModels;
using ViewGrid.Presentation.Views;

namespace ViewGrid.Presentation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Ctrl+, は MainWindow 自身に紐付けたいが、 KeyBindings 内の ElementName 参照が
        // NameScope の都合で解決しないため、 ここで KeyDown を直接購読する。 既存
        // Ctrl+Z/Y/O/N/E は VM コマンドへの DataContext バインドなので KeyBindings で
        // OK だが、 設定ダイアログ起動は View 側にあるためこちらの経路を使う。
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemComma && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            OnSettingsClicked(this, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
        }
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

    /// <summary>「ファイル → 終了」: Window を閉じる。</summary>
    private void OnExitClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>
    /// 「ファイル → 設定...」 (Ctrl+,) で設定ダイアログを開く。 VM は DI から取得。
    /// 即時適用 + 自動保存型なので OK / Cancel ボタンはなく、 ダイアログを閉じても変更は確定済み。
    /// </summary>
    private async void OnSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 名前空間衝突回避のため Avalonia.Application を完全修飾。
        // (using ViewGrid.Application.ViewModels の影響で `Application` 単体は VG 側に解決される)
        if (Avalonia.Application.Current is not App app || app.Services is null) return;
        await SettingsDialog.ShowAsync(this, app.Services);
    }

    /// <summary>
    /// 「ファイル → ワークスペース切替...」: 切替ダイアログを開く。 確定すると active.json 書き換え +
    /// プロセス再起動 (--workspace=&lt;name&gt; 引数付き) で新ワークスペースのデータでアプリが再開する。
    /// </summary>
    private async void OnWorkspaceSwitchClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Avalonia.Application.Current is not App app || app.Services is null) return;
        await WorkspaceSwitchDialog.ShowAsync(this, app.Services);
    }

    /// <summary>「ヘルプ → ViewGrid について」: 簡易な情報ダイアログ。</summary>
    private async void OnAboutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "ViewGrid について",
            Width = 360,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 12,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = "ViewGrid", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new Avalonia.Controls.TextBlock { Text = "画像をグリッドに配置するシンプルなツール。", Opacity = 0.75 },
                    new Avalonia.Controls.TextBlock { Text = "© 2026 ViewGrid", FontSize = 11, Opacity = 0.55, Margin = new Avalonia.Thickness(0, 8, 0, 0) },
                },
            },
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// 履歴 ListBox の各項目（DataTemplate 内 Grid）に hover した時、VM の
    /// <see cref="MainWindowViewModel.HoveredHistoryIndex"/> を更新する。
    /// VM 側で「現在位置との差」を計算して Lo/Hi/Direction に反映され、
    /// MultiBinding Converter で範囲内の各項目に Undo=赤 / Redo=緑 の背景が描画される。
    /// </summary>
    private void OnHistoryItemPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is Control ctrl && ctrl.DataContext is HistoryEntry entry
            && DataContext is MainWindowViewModel vm)
        {
            vm.HoveredHistoryIndex = entry.Index;
        }
    }

    /// <summary>
    /// 項目から pointer が離れたら hover プレビューを解除する。
    /// 別項目への移動の場合、先に Exit、続けて Enter が発火するため、
    /// 順序として「null → 新 Index」で正しく更新される。
    /// </summary>
    private void OnHistoryItemPointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.HoveredHistoryIndex = null;
        }
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
