using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="ProtectedRegionFillMode"/> を ComboBox 表示用の日本語ラベルに変換する。
/// </summary>
public sealed class ProtectedRegionFillModeLabelConverter : IValueConverter
{
    public static readonly ProtectedRegionFillModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ProtectedRegionFillMode mode ? Label(mode) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Label(ProtectedRegionFillMode mode) => mode switch
    {
        ProtectedRegionFillMode.White => "白 #FFFFFF",
        ProtectedRegionFillMode.Black => "黒 #000000",
        ProtectedRegionFillMode.Transparent => "透明 (α=0)",
        ProtectedRegionFillMode.Custom => "カスタム",
        _ => mode.ToString(),
    };
}
