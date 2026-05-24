using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewGrid.Presentation.Controls;

/// <summary>
/// <see cref="NumericUpDown"/> の入力欄を空にした状態でフォーカスが外れたとき、
/// <c>Value</c> が null のままだと VM 側プロパティも null のまま残って後段の保存処理で
/// <c>?? fallback</c> に毎回頼ることになる。 BlankGuard はフォーカス離脱時に null を
/// レンジ内の自然なゼロ値 (= <c>Clamp(0, Minimum, Maximum)</c>) に戻すことで、
/// 編集後の状態を「常に有効な数値」 に正規化する UX フォールバップ。
/// </summary>
/// <remarks>
/// <para>
/// バインディング例外 (Value=decimal? → int 変換失敗) は VM プロパティを <c>int?</c> に
/// する根本対処で防ぐ前提。 BlankGuard はその上に乗る UX 改善。
/// </para>
/// <para>
/// フォールバック値のヒューリスティクス: <c>Clamp(0, Minimum, Maximum)</c>。
/// <list type="bullet">
///   <item>負値も許容するフィールド (例: PixelOffset -4096..4096) → 0 ("オフセットなし")</item>
///   <item>正値専用 (例: Rows/Cols/CanvasSize 1..N) → Minimum (=1, "最小有効値")</item>
///   <item>0 起点 (例: ManualCropPixelX 0..N) → 0</item>
/// </list>
/// レンジ下限が常に正しい既定値とは限らない (PixelOffset の Minimum=-4096 は範囲制約であって
/// 既定値ではない) ので、 一律 Minimum 戻しは行わない。
/// </para>
/// <para>
/// 設計判断: View ごとに <c>LostFocus="..."</c> を書く方式は記述漏れを招くため、
/// <see cref="CommonStyles"/> のグローバル <c>Style Selector="NumericUpDown"</c> で
/// 一括有効化する。 既存 <see cref="FocusOnVisible"/> 添付プロパティと同パターン。
/// </para>
/// </remarks>
public static class NumericUpDownBlankGuard
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>(
            "IsEnabled", typeof(NumericUpDownBlankGuard));

    public static bool GetIsEnabled(NumericUpDown element) =>
        element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(NumericUpDown element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    static NumericUpDownBlankGuard()
    {
        IsEnabledProperty.Changed.AddClassHandler<NumericUpDown>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(NumericUpDown control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
            control.LostFocus += OnLostFocus;
        else
            control.LostFocus -= OnLostFocus;
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not NumericUpDown nud || nud.Value is not null) return;
        // 0 がレンジ内なら 0、 そうでなければ近い側のレンジ境界に丸める。
        // Math.Clamp の引数順 (value, min, max) でそのまま意図を表現できる。
        nud.Value = Math.Clamp(0m, nud.Minimum, nud.Maximum);
    }
}
