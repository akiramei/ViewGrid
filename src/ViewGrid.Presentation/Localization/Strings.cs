using System.Globalization;
using System.Resources;

namespace ViewGrid.Presentation.Localization;

/// <summary>
/// <see cref="Strings.resx"/> + <c>Strings.en.resx</c> の satellite assembly を読み出す
/// ResourceManager のラッパ。 自動生成された Designer.cs の代わりに手書きで最小限の
/// アクセサを提供する (LocService の indexer 経由で参照されるため個別プロパティは不要)。
/// </summary>
/// <remarks>
/// ResourceManager の "BaseName" は &lt;default namespace&gt; + フォルダ + ファイル名で構成される。
/// Presentation の default namespace = "ViewGrid.Presentation" なので、
/// <c>Localization/Strings.resx</c> は <c>ViewGrid.Presentation.Localization.Strings</c>。
/// </remarks>
public static class Strings
{
    public static ResourceManager ResourceManager { get; } = new(
        "ViewGrid.Presentation.Localization.Strings",
        typeof(Strings).Assembly);

    /// <summary>現在の Culture (LocService が <see cref="LocService.SetCulture"/> で同期する)。</summary>
    public static CultureInfo? Culture { get; set; }
}
