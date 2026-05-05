namespace ViewGrid.Core.Settings;

/// <summary>
/// アクセント色プリセット 1 件。 1 つの色相 (Sky / Emerald 等) に対して、 Light テーマと
/// Dark テーマでそれぞれ視認性を最適化した 7 段階パレットを保持する。 起動時 + 設定変更時に
/// <see cref="AccentColorPalette"/> の 7 キーを <c>App.axaml</c> の ThemeDictionaries
/// 内の対応キーへ書き戻すことで、 全 <c>{DynamicResource SystemAccentColor*}</c> 参照を更新する。
/// </summary>
/// <param name="Id">永続化キー (例: "Sky"、 "Emerald")。 <see cref="AppSettings.AccentColor"/> がこれを保持。</param>
/// <param name="DisplayName">UI 表示用の日本語ラベル。</param>
/// <param name="SwatchColor">設定ダイアログの色ドット表示用 (= Light テーマの base 色)。</param>
/// <param name="Light">Light テーマ用 7 色パレット (base = Tailwind X-500)。</param>
/// <param name="Dark">Dark テーマ用 7 色パレット (base = Tailwind X-400)。</param>
public sealed record AccentColorPreset(
    string Id,
    string DisplayName,
    string SwatchColor,
    AccentColorPalette Light,
    AccentColorPalette Dark);
