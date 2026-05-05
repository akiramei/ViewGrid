using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ViewGrid.Presentation.Views;

public partial class PlacementInspectorView : UserControl
{
    /// <summary>
    /// 配置を削除するコマンドを外部から注入するための AvaloniaProperty。
    /// 親 (GridWorkspaceView) の RemoveSelectedPlacementCommand を Inspector 内の保存バーに統合表示するために使う。
    /// UserControl 独自 NameScope のため ElementName binding では親 VM に到達できないので
    /// View プロパティ経由で受け渡す。
    /// </summary>
    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<PlacementInspectorView, ICommand?>(nameof(RemoveCommand));

    public ICommand? RemoveCommand
    {
        get => GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public PlacementInspectorView()
    {
        InitializeComponent();
    }
}
