namespace ViewGrid.Core.Entities;

/// <summary>
/// UI で対象色を選ぶときのプリセット種別。
/// <see cref="Custom"/> は将来の拡張用（HEX 入力 / 画像クリックピッカー）。Phase 1 では使わない。
/// </summary>
public enum AutoCropPreset
{
    White,
    Black,
    Transparent,
    Custom,
}
