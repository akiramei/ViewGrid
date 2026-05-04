using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ViewGrid.Application.History;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="ViewGrid.Application.History.HistoryEntry.IsApplied"/> を、履歴エントリ前の
/// 「適用済み / 取消済み」を示すグリフ文字に変換する。
/// 適用済み → "✓"、取消済み → "○"。
/// </summary>
public sealed class BoolToHistoryGlyphConverter : IValueConverter
{
    public static readonly BoolToHistoryGlyphConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? "✓" : "○";
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// <see cref="ViewGrid.Application.History.HistoryEntry.IsApplied"/> を、リスト項目の表示濃度
/// （Opacity 1.0 / 0.5）に変換する。取消済みエントリは薄字で「現在生きていない」ことを示す。
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? 1.0 : 0.5;
        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// hover プレビュー（Phase 3）用の MultiBinding Converter。
/// 入力 4 値: <c>HistoryEntry.Index</c>, <c>HoveredJumpRangeLo</c>, <c>HoveredJumpRangeHi</c>, <c>HoveredJumpDirection</c>。
/// 出力: <see cref="IBrush"/>。範囲外なら <see cref="Brushes.Transparent"/>、
/// Undo 方向なら半透明赤、Redo 方向なら半透明緑。
/// </summary>
public sealed class HoveredJumpRangeBackgroundConverter : IMultiValueConverter
{
    public static readonly HoveredJumpRangeBackgroundConverter Instance = new();

    // Undo は暖色（赤系、警告/取消の意味）、Redo はアクセント色のティール（進む方向）。
    // 緑（#33CC66）はダーク背景との組み合わせが不調和になりやすいため、アクセント色 sky-500
    // (#0EA5E9) の半透明に統一。Undo / Redo の暖色 vs 寒色 で方向の意味は引き続き伝わる。
    private static readonly IBrush UndoBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xDC, 0x26, 0x26));
    private static readonly IBrush RedoBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x0E, 0xA5, 0xE9));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 4) return Brushes.Transparent;
        if (values[0] is not int index) return Brushes.Transparent;
        if (values[1] is not int lo) return Brushes.Transparent;
        if (values[2] is not int hi) return Brushes.Transparent;
        if (values[3] is not JumpDirection dir) return Brushes.Transparent;

        if (dir == JumpDirection.None) return Brushes.Transparent;
        if (lo < 0 || hi < 0 || index < lo || index > hi) return Brushes.Transparent;

        return dir == JumpDirection.Undo ? UndoBrush : RedoBrush;
    }
}
