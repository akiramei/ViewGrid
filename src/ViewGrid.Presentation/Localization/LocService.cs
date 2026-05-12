using System.ComponentModel;
using System.Globalization;

namespace ViewGrid.Presentation.Localization;

/// <summary>
/// 言語切替を支える Singleton サービス。 INPC の indexer ("Item[]") 通知で
/// XAML の全 localization binding を一括再評価させ、 アプリ再起動なしで UI を更新する。
/// </summary>
/// <remarks>
/// <para>使い方:</para>
/// <list type="bullet">
///   <item>XAML: <c>Text="{loc:Tr Menu_File}"</c> (TrExtension が <c>this[Key]</c> を購読)</item>
///   <item>code-behind / VM: <c>LocService.Instance["Menu_File"]</c> で文字列取得</item>
///   <item>言語切替: <c>LocService.Instance.SetCulture(new CultureInfo("en"))</c></item>
/// </list>
/// <para>satellite assembly (Strings.en.resources.dll) は build で自動生成されるため、
/// 開発側は <c>Strings.resx</c> (日本語、 fallback) と <c>Strings.en.resx</c> (英語) を編集するだけで足りる。</para>
/// </remarks>
public sealed class LocService : INotifyPropertyChanged
{
    public static LocService Instance { get; } = new();

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public CultureInfo Culture => _culture;

    /// <summary>
    /// XAML 用 indexer。 キー未定義 / リソース読み込み失敗時は <c>!Key!</c> を返して開発者に
    /// 視覚的に通知する (空文字や例外で UI が壊れるよりトラブルシュート性が高い)。
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            try
            {
                return Strings.ResourceManager.GetString(key, _culture) ?? $"!{key}!";
            }
            catch
            {
                return $"!{key}!";
            }
        }
    }

    /// <summary>
    /// Culture を切り替えて全 indexer binding を再評価させる。 同じ culture を渡された場合は
    /// 何もしない (PropertyChanged 連鎖の無駄打ち抑止)。
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (_culture.Equals(culture)) return;
        _culture = culture;
        Strings.Culture = culture;

        // "Item[]" は WPF/Avalonia の INPC 慣例で「全 indexer の値が変わった」 を表す特殊名。
        // バインディングシステムがこの値を見て indexer source の再評価を全て発火する。
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
