using System.Globalization;

namespace ViewGrid.Application.Localization;

/// <summary>
/// テスト・デザイン時用の no-op 実装。 リソースを引かず、 キーをそのまま (またはキー +
/// プレースホルダ展開結果を) 返す。 表示文言の言語に依存しないアサーションを書ける。
/// </summary>
public sealed class NullLocalizationService : ILocalizationService
{
    public string this[string key] => key ?? string.Empty;

    public string Format(string key, params object?[] args) =>
        args is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, "{0}({1})", key, string.Join(",", args))
            : key ?? string.Empty;
}
