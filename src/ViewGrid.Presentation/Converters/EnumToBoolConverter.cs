using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// enum 値と <see cref="bool"/> を相互変換する。 RadioButton グループのように「複数の選択肢を
/// 単一の enum プロパティへ two-way バインドする」 用途で使う。
/// <para>
/// <c>Convert</c>: バインド元 enum 値が <c>ConverterParameter</c>（enum メンバ名）と一致すれば
/// <c>true</c>（= そのラジオがチェック状態）。
/// </para>
/// <para>
/// <c>ConvertBack</c>: チェックされた（<c>true</c>）ときだけ対応する enum 値を書き戻す。
/// <b>未チェック（<c>false</c>）の書き戻しは <see cref="BindingOperations.DoNothing"/> で握りつぶす</b>。
/// これにより、 RadioButton グループが排他制御で選択解除側へ送る <c>false</c> がソースプロパティを
/// 汚染せず、 DataContext 切替時に状態が意図せず初期化される問題を防ぐ。
/// </para>
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && parameter is string name
           && string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // チェックされた選択肢だけが enum 値を書き戻す。 未チェック側の false は無視する
        // (RadioButton グループの排他制御による誤書き戻しを遮断)。
        if (value is true && parameter is string name)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (enumType.IsEnum && Enum.TryParse(enumType, name, ignoreCase: false, out var parsed))
                return parsed;
        }
        return BindingOperations.DoNothing;
    }
}
