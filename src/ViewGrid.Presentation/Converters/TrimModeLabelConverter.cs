using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="TrimMode"/> を ComboBox 表示用ラベルに変換する。
/// 現在 culture の resx から引く。 プレビュー / PNG 出力で共通の選択肢として使う。
/// </summary>
public sealed class TrimModeLabelConverter : IValueConverter
{
    public static readonly TrimModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TrimMode mode ? Label(mode) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Label(TrimMode mode) => mode switch
    {
        TrimMode.None => LocAccessor.Current["Output_Trim_None"],
        TrimMode.OccupiedCells => LocAccessor.Current["Output_Trim_OccupiedCells"],
        TrimMode.DrawnPixels => LocAccessor.Current["Output_Trim_DrawnPixels"],
        _ => mode.ToString(),
    };
}
