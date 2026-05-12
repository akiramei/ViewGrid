namespace ViewGrid.Application.Localization;

/// <summary>
/// バリアント (論理コピー) の UI 呼称を表す resx キー定数集。
/// 呼出側は <c>_loc[Terminology.VariantKey]</c> や <c>LocAccessor.Current[Terminology.VariantUnnamedKey]</c>
/// のように indexer に渡して現在 culture の文字列を取得する。
/// </summary>
/// <remarks>
/// <para>i18n Phase 3+ で従来の <c>const string Variant = "バリアント"</c> 等の固定文字列定数から
/// resx キー定数へ移行した。 これにより日英動的切替で 「バリアント」 ↔ 「Variant」 が即時反映される
/// (computed property 経由の場合は VM が <see cref="ILocalizationService"/> 通知を購読する必要がある)。</para>
/// <para>コード識別子 (<c>ImageCopy</c> / <c>CopyName</c> / <c>CopyId</c> 等) はデータモデル巻き込みを
/// 避けるため触らない方針は維持。</para>
/// </remarks>
public static class Terminology
{
    /// <summary>論理コピーの単数形 resx キー (例: 日本語 「バリアント」 / 英語 "Variant")。</summary>
    public const string VariantKey = "Term_Variant";

    /// <summary>新規作成時の自動採番接頭辞 resx キー。 <c>$"{loc[VariantPrefixKey]} {N}"</c> で 「バリアント 3」。</summary>
    public const string VariantPrefixKey = "Term_VariantPrefix";

    /// <summary>無名のバリアントを表す表示用ラベル resx キー (リネーム履歴の前後比較等で使用)。</summary>
    public const string VariantUnnamedKey = "Term_VariantUnnamed";

    /// <summary>不明・解決不能なバリアントのフォールバック表示 resx キー。</summary>
    public const string VariantUnknownKey = "Term_VariantUnknown";

    /// <summary>
    /// DB / 永続化用の culture invariant な「バリアント」 文字列。
    /// 表示文字列ではなく **データ** (例: <see cref="UseCases.ForkPlacementVariantUseCase"/>
    /// が DB に書き込む派生バリアント名のフォールバック) として使う。
    /// 言語切替で値が変わると既存 DB との整合性が崩れるため固定。 翻訳キーは <see cref="VariantKey"/>。
    /// </summary>
    public const string VariantInvariant = "バリアント";
}
