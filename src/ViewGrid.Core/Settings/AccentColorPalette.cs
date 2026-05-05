namespace ViewGrid.Core.Settings;

/// <summary>
/// アクセント色の 7 段階パレット。 App.axaml の <c>SystemAccentColor*</c> ResourceDictionary
/// に対応する 7 キー (base + Dark1/2/3 + Light1/2/3) を HEX 文字列で保持する。
/// 1 プリセット (例: Sky) につき Light テーマ用 / Dark テーマ用の 2 セット必要 (= <see cref="AccentColorPreset"/>)。
/// </summary>
/// <param name="Color">SystemAccentColor (base)。</param>
/// <param name="Dark1">SystemAccentColorDark1 (一段濃く)。</param>
/// <param name="Dark2">SystemAccentColorDark2 (二段濃く)。</param>
/// <param name="Dark3">SystemAccentColorDark3 (三段濃く)。</param>
/// <param name="Light1">SystemAccentColorLight1 (一段薄く)。</param>
/// <param name="Light2">SystemAccentColorLight2 (二段薄く)。</param>
/// <param name="Light3">SystemAccentColorLight3 (三段薄く)。</param>
public sealed record AccentColorPalette(
    string Color,
    string Dark1,
    string Dark2,
    string Dark3,
    string Light1,
    string Light2,
    string Light3);
