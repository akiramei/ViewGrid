namespace ViewGrid.Application.Localization;

/// <summary>
/// ユーザー可視文字列の用語集約。
/// 「論理コピー」(<c>ImageCopy</c> エンティティの UI 上の呼称) を「バリアント」へ統一する目的で導入。
/// 後で「派生」「編集プリセット」等へ揺らす場合はここを 1 箇所変更すれば全画面に反映される。
/// コード識別子（<c>ImageCopy</c> / <c>CopyName</c> / <c>CopyId</c> 等）はデータモデル巻き込みを避けるため触らない。
/// </summary>
public static class Terminology
{
    /// <summary>論理コピーの単数形（例: 「バリアント」）。</summary>
    public const string Variant = "バリアント";

    /// <summary>新規作成時の自動採番接頭辞。<c>$"{VariantPrefix} {N}"</c> で「バリアント 3」になる。</summary>
    public const string VariantPrefix = "バリアント";

    /// <summary>無名のバリアントを表す表示用ラベル（リネーム履歴の前後比較等で使用）。</summary>
    public const string VariantUnnamed = "(無名)";

    /// <summary>不明・解決不能なバリアントのフォールバック表示。</summary>
    public const string VariantUnknown = "(不明なバリアント)";
}
