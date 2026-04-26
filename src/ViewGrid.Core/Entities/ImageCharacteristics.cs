namespace ViewGrid.Core.Entities;

/// <summary>
/// 画像特性の集合（スケーリング・トリミング・アライメント）。
/// </summary>
public readonly record struct ImageCharacteristics(
    ScalingMode ScalingMode,
    TrimmingAnchor TrimmingAnchor,
    Alignment Alignment)
{
    public static ImageCharacteristics Default { get; } = new(
        ScalingMode.UniformContain,
        TrimmingAnchor.Center,
        Alignment.Center);
}
