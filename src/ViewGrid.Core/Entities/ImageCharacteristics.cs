namespace ViewGrid.Core.Entities;

/// <summary>
/// 画像特性の集合（スケーリング・アライメント）。
/// 旧版は <c>TrimmingAnchor</c> を持っていたが、CSS background-position 等の業界標準に倣い
/// 「位置決め」と「トリミング」を 1 つの <see cref="Alignment"/> に統一した。
/// </summary>
public readonly record struct ImageCharacteristics(
    ScalingMode ScalingMode,
    Alignment Alignment)
{
    public static ImageCharacteristics Default { get; } = new(
        ScalingMode.UniformContain,
        Alignment.Center);
}
