using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// 設定ダイアログのアクセント色プリセット選択 UI で「現在選択中の色ドットだけ枠を太く描画」
/// するための MultiBinding。 values = [プリセット.Id (string), VM.AccentColor (string)]。
/// 等しければ <see cref="SelectedThickness"/>、 異なれば <see cref="UnselectedThickness"/>。
/// </summary>
public sealed class AccentSelectionThicknessConverter : IMultiValueConverter
{
    public static readonly AccentSelectionThicknessConverter Instance = new();

    public static readonly Thickness SelectedThickness = new(2);
    public static readonly Thickness UnselectedThickness = new(0);

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return UnselectedThickness;
        var a = values[0] as string;
        var b = values[1] as string;
        return string.Equals(a, b, StringComparison.Ordinal)
            ? SelectedThickness
            : UnselectedThickness;
    }
}
