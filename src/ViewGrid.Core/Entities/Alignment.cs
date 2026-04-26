namespace ViewGrid.Core.Entities;

/// <summary>
/// セル内での画像配置位置（スケーリング後の位置調整）。
/// </summary>
public readonly record struct Alignment(AnchorX X, AnchorY Y)
{
    public static Alignment Center { get; } = new(AnchorX.Center, AnchorY.Center);
}
