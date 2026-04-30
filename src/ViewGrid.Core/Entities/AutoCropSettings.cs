namespace ViewGrid.Core.Entities;

/// <summary>
/// 単色余白の自動トリミング設定。論理コピーが <see cref="ImageCopy.AutoCrop"/>
/// にこの値を保持していると、外周から <see cref="TargetColorArgb"/> ＋ <see cref="Threshold"/>
/// に一致するピクセルを矩形維持で削り、内側の bbox だけを描画対象とする。
/// <para>
/// プリセット（白・黒・透明）は static factory で取得する。null は機能 OFF を表す。
/// </para>
/// </summary>
public readonly record struct AutoCropSettings(uint TargetColorArgb, byte Threshold)
{
    /// <summary>白 (#FFFFFFFF) を対象色、閾値 8 のプリセット。</summary>
    public static AutoCropSettings White { get; } = new(0xFFFFFFFFu, 8);

    /// <summary>黒 (#FF000000) を対象色、閾値 8 のプリセット。</summary>
    public static AutoCropSettings Black { get; } = new(0xFF000000u, 8);

    /// <summary>
    /// 透明 (α=0) を対象色、閾値 0 のプリセット。<see cref="TargetColorArgb"/>
    /// の α が 0 の場合は判定が「α &lt;= Threshold」のみになり、RGB は無視される。
    /// </summary>
    public static AutoCropSettings Transparent { get; } = new(0x00000000u, 0);
}
