using System.ComponentModel;

namespace ViewGrid.Application.Localization;

/// <summary>
/// VM から resx ベースの i18n 文字列を引くための抽象。 Presentation 層の
/// <c>LocService</c> が実体を提供し、 DI で Singleton 注入される。
/// </summary>
/// <remarks>
/// <para>VM が直接 <c>LocService.Instance</c> を参照すると Application → Presentation の
/// 依存方向が逆転するため、 この抽象を経由する。</para>
/// <para>テストでは <see cref="NullLocalizationService"/> を渡すことで「キー文字列を
/// そのまま返す」 動作になり、 アサーションは表示文言の言語に依存しない。</para>
/// <para><see cref="INotifyPropertyChanged"/> を継承しているため、 言語切替時に
/// <c>"Item[]"</c> 通知を受け取って computed property を再評価できる
/// (例: <c>MainWindowViewModel.CurrentHints</c>)。</para>
/// </remarks>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>キーから現在 culture の表示文字列を取得する。</summary>
    string this[string key] { get; }

    /// <summary>
    /// キーから取得した書式文字列に <see cref="string.Format(System.IFormatProvider,string,object?[])"/>
    /// を適用する。 引数は <c>{0},{1},...</c> プレースホルダで参照。
    /// </summary>
    string Format(string key, params object?[] args);
}
