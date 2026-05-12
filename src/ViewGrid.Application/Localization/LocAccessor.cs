namespace ViewGrid.Application.Localization;

/// <summary>
/// DI コンストラクタで <see cref="ILocalizationService"/> を受け取れない場面 (リスト要素として
/// 大量に <c>new</c> される ItemViewModel 等) 用の Singleton アクセサ。
/// </summary>
/// <remarks>
/// <para>原則は DI コンストラクタ注入。 本アクセサを使うのは「親 VM 経由の手渡しが
/// 著しく非効率な場合」 に限る (例: <c>CopyCandidateViewModel</c> / <c>CandidateGroupViewModel</c>)。</para>
/// <para>アプリ起動時に <c>App.OnFrameworkInitializationCompleted</c> で
/// <c>LocAccessor.Current = services.GetRequiredService&lt;ILocalizationService&gt;()</c> を呼んで初期化。
/// 未初期化のまま参照された場合 (テスト・デザイン時) は <see cref="NullLocalizationService"/>
/// が返るのでクラッシュしない。</para>
/// </remarks>
public static class LocAccessor
{
    public static ILocalizationService Current { get; set; } = new NullLocalizationService();
}
