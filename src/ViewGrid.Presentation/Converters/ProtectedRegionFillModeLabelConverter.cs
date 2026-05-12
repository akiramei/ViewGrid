using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="ProtectedRegionFillMode"/> を ComboBox 表示用ラベルに変換する。 現在 culture の resx から引く。
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
        ProtectedRegionFillMode.White => LocAccessor.Current["ColorPreset_White"],
        ProtectedRegionFillMode.Black => LocAccessor.Current["ColorPreset_Black"],
        ProtectedRegionFillMode.Transparent => LocAccessor.Current["ColorPreset_Transparent"],
        ProtectedRegionFillMode.None => LocAccessor.Current["RegionFillMode_None"],
        ProtectedRegionFillMode.Custom => LocAccessor.Current["ColorPreset_Custom"],
        _ => mode.ToString(),
    };
}
