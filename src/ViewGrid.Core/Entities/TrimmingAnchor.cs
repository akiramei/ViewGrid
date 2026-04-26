namespace ViewGrid.Core.Entities;

/// <summary>
/// スケーリング不可時にどこを残してトリミングするか。
/// </summary>
public readonly record struct TrimmingAnchor(AnchorX X, AnchorY Y)
{
    public static TrimmingAnchor Center { get; } = new(AnchorX.Center, AnchorY.Center);
    public static TrimmingAnchor TopCenter { get; } = new(AnchorX.Center, AnchorY.Top);
    public static TrimmingAnchor TopLeft { get; } = new(AnchorX.Left, AnchorY.Top);
}
