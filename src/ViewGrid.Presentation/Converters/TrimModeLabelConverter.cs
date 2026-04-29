using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="TrimMode"/> を ComboBox 表示用の日本語ラベルに変換する。
/// プレビュー / PNG 出力で共通の選択肢として使う。
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
        TrimMode.None => "なし（キャンバス全面）",
        TrimMode.OccupiedCells => "占有セルで切り出し",
        TrimMode.DrawnPixels => "描画ピクセルで切り出し",
        _ => mode.ToString(),
    };
}
