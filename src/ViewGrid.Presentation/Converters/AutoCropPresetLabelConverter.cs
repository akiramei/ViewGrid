using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="AutoCropPreset"/> を ComboBox 表示用ラベルに変換する。 現在 culture の resx から引く。
/// </summary>
public sealed class AutoCropPresetLabelConverter : IValueConverter
{
    public static readonly AutoCropPresetLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AutoCropPreset preset ? Label(preset) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Label(AutoCropPreset preset) => preset switch
    {
        AutoCropPreset.White => LocAccessor.Current["ColorPreset_White"],
        AutoCropPreset.Black => LocAccessor.Current["ColorPreset_Black"],
        AutoCropPreset.Transparent => LocAccessor.Current["ColorPreset_Transparent"],
        AutoCropPreset.Custom => LocAccessor.Current["ColorPreset_Custom"],
        _ => preset.ToString(),
    };
}
