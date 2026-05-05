using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="OutputMode"/> を出力設定 Expander のヘッダー表示用日本語ラベルに変換する。
/// 「通常」「写真ボード」のように短く、現在状態が一目で分かるラベル。
/// </summary>
public sealed class OutputModeLabelConverter : IValueConverter
{
    public static readonly OutputModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OutputMode mode ? Label(mode) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Label(OutputMode mode) => mode switch
    {
        OutputMode.Normal => "通常",
        OutputMode.PhotoBoard => "写真ボード",
        _ => mode.ToString(),
    };
}
