using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="OutputMode"/> を出力設定 Expander のヘッダー表示用ラベルに変換する。
/// 現在 culture の resx から短く現在状態を示すラベルを引く。
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
        OutputMode.Normal => LocAccessor.Current["Output_Mode_Normal"],
        OutputMode.PhotoBoard => LocAccessor.Current["Output_Mode_PhotoBoard"],
        _ => mode.ToString(),
    };
}
