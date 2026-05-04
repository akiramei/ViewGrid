using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="AutoCropPreset"/> を ComboBox 表示用の日本語ラベルに変換する。
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
        AutoCropPreset.White => "白 #FFFFFF",
        AutoCropPreset.Black => "黒 #000000",
        AutoCropPreset.Transparent => "透明 (α=0)",
        AutoCropPreset.Custom => "カスタム",
        _ => preset.ToString(),
    };
}
