using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="ScalingMode"/> を ComboBox 表示用の日本語ラベルに変換する。
/// </summary>
public sealed class ScalingModeLabelConverter : IValueConverter
{
    public static readonly ScalingModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ScalingMode mode ? Label(mode) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Label(ScalingMode mode) => mode switch
    {
        ScalingMode.None => "原寸固定",
        ScalingMode.UniformContain => "アスペクト維持（収める）",
        ScalingMode.UniformContainShrinkOnly => "縮小のみ",
        ScalingMode.UniformContainEnlargeOnly => "拡大のみ",
        // 旧表記「アスペクト維持（埋める）」はユーザー認識（=収まる）と挙動（=見切れる）が乖離していた。
        // 「覆う・切り取り」で「全面を覆うため画像の一部が切れる」ことを明示する。
        ScalingMode.UniformCover => "アスペクト維持（覆う・切り取り）",
        ScalingMode.Fill => "完全充填（縦横独立）",
        _ => mode.ToString(),
    };
}
