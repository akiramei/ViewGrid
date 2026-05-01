using System;
using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace ViewGrid.Presentation.Converters;

/// <summary>
/// <see cref="ImmutableArray{T}"/> をカンマ区切り文字列に変換する。
/// グリッドプロパティ表示で <c>ColWeights</c> / <c>RowWeights</c>（int 配列）を
/// "2, 1, 1" のような可読形式で表示するために使う。
/// </summary>
public sealed class ImmutableArrayJoinConverter : IValueConverter
{
    public static readonly ImmutableArrayJoinConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable enumerable)
            return string.Join(", ", enumerable.Cast<object?>().Select(x => x?.ToString() ?? string.Empty));
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// <see cref="ImmutableArray{Boolean}"/> 内の <c>true</c> 件数を「N/M ロック中」の形式で表示する。
/// グリッドプロパティで列・行のロック状態を要約するために使う。
/// 全アンロック時は「なし」を返してノイズを減らす。
/// </summary>
public sealed class LockedCountConverter : IValueConverter
{
    public static readonly LockedCountConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable enumerable)
        {
            var items = enumerable.Cast<bool>().ToArray();
            var locked = items.Count(b => b);
            if (locked == 0) return $"なし ({items.Length} 件中)";
            return $"{locked}/{items.Length} ロック中";
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
