using System;
using System.Globalization;
using Avalonia.Data.Converters;

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
