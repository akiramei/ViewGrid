namespace ViewGrid.Core.Entities;

/// <summary>
/// 画像をセル（dst 矩形）に対してどう拡大・縮小して描画するかのモード。
/// CSS object-fit に近い意味論を持つ。
/// </summary>
public enum ScalingMode
{
    /// <summary>原寸維持。はみ出しは <see cref="TrimmingAnchor"/>、空白は <see cref="Alignment"/> で位置決め。</summary>
    None = 0,

    /// <summary>アスペクト維持で dst に収める（fit-inside）。空白は <see cref="Alignment"/>。</summary>
    UniformContain = 1,

    /// <summary>アスペクト維持で縮小のみ。元画像が dst より小さい場合は原寸。</summary>
    UniformContainShrinkOnly = 2,

    /// <summary>アスペクト維持で拡大のみ。元画像が dst より大きい場合は原寸。</summary>
    UniformContainEnlargeOnly = 3,

    /// <summary>アスペクト維持で dst を埋める（fit-cover）。はみ出しは <see cref="TrimmingAnchor"/>。</summary>
    UniformCover = 4,

    /// <summary>異方スケールで dst を完全充填（aspect 破壊）。スキャン見開き等に最適。</summary>
    Fill = 5,
}
