using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="ScalingMode"/> を ComboBox 表示用ラベルに変換する。 現在 culture の resx から引く。
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
        ScalingMode.None => LocAccessor.Current["Scaling_None"],
        ScalingMode.UniformContain => LocAccessor.Current["Scaling_UniformContain"],
        ScalingMode.UniformContainShrinkOnly => LocAccessor.Current["Scaling_UniformContainShrinkOnly"],
        ScalingMode.UniformContainEnlargeOnly => LocAccessor.Current["Scaling_UniformContainEnlargeOnly"],
        // 旧表記「アスペクト維持（埋める）」はユーザー認識（=収まる）と挙動（=見切れる）が乖離していた。
        // 「覆う・切り取り」で「全面を覆うため画像の一部が切れる」ことを明示する。
        ScalingMode.UniformCover => LocAccessor.Current["Scaling_UniformCover"],
        ScalingMode.Fill => LocAccessor.Current["Scaling_Fill"],
        _ => mode.ToString(),
    };
}
