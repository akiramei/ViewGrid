using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// HEX 文字列 (例: "#0EA5E9") を <see cref="IBrush"/> (SolidColorBrush) に変換する。
/// 設定ダイアログのアクセント色プリセット選択 UI で、 各色ドットの背景塗りに使う。
/// 不正な入力は <see cref="Brushes.Transparent"/> を返す。
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrEmpty(hex))
            return Brushes.Transparent;

        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
