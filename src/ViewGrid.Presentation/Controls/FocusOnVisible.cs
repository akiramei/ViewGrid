using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ViewGrid.Presentation.Controls;

/// <summary>
/// Attached Property 版 AutoFocus。「<c>IsVisible</c> が true になった瞬間に
/// 自身へフォーカスを取り、<see cref="TextBox.SelectAll"/> 可能なら全選択する」挙動を
/// 任意の <see cref="Control"/> に注入する。<see cref="TextBox"/> サブクラス化する案では
/// Avalonia の<b>実型一致スタイル</b>に阻まれて Fluent テーマの既定 ControlTemplate が
/// 当たらず、枠・背景・キャレットがすべて欠落した不可視ステートになっていた。
/// 本 Attached Property 版なら標準 <c>TextBox</c> をそのまま使えるためテーマが正常適用される。
/// </summary>
/// <remarks>
/// XAML 例:
/// <code>
/// xmlns:controls="clr-namespace:ViewGrid.Presentation.Controls"
/// ...
/// &lt;TextBox controls:FocusOnVisible.IsEnabled="True"
///          IsVisible="{Binding IsEditing}" /&gt;
/// </code>
/// <para>
/// 仕掛け: 添付プロパティを true にすると、対象 Control の <see cref="AvaloniaObject.PropertyChanged"/>
/// を購読して <see cref="Visual.IsVisibleProperty"/> 変化を観測する。false → true に転じた瞬間に
/// <see cref="Dispatcher.UIThread"/> + <see cref="DispatcherPriority.Loaded"/> でレイアウト完了後の
/// タイミングへフォーカス処理をポストする。Loaded 優先度は Measure / Arrange の後に走るため、
/// ListBox の PointerReleased 等にフォーカスを奪い返される現象を回避できる。
/// 初期値が true（最初から表示状態）でも一度フォーカスを取りに行く。
/// </para>
/// </remarks>
public static class FocusOnVisible
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled", typeof(FocusOnVisible));

    public static bool GetIsEnabled(Control element) =>
        element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    static FocusOnVisible()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            control.PropertyChanged += OnControlPropertyChanged;
            if (control.IsVisible)
                ScheduleFocus(control);
        }
        else
        {
            control.PropertyChanged -= OnControlPropertyChanged;
        }
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Control control) return;
        if (e.Property != Visual.IsVisibleProperty) return;
        if (!e.GetNewValue<bool>()) return;
        ScheduleFocus(control);
    }

    private static void ScheduleFocus(Control control)
    {
        // DispatcherPriority.Loaded は Measure/Arrange 完了後に実行されるため、
        // attach / visibility 変化直後に Focus() が無効化される現象を回避できる。
        Dispatcher.UIThread.Post(() =>
        {
            if (!control.IsEffectivelyVisible) return;
            control.Focus();
            if (control is TextBox tb)
                tb.SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
