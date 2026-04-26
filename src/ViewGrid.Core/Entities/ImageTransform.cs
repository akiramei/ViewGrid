namespace ViewGrid.Core.Entities;

/// <summary>
/// 画像の変形（回転・反転）設定。
/// </summary>
public readonly record struct ImageTransform(Rotation Rotation, bool FlipX, bool FlipY)
{
    public static ImageTransform Identity { get; } = new(Rotation.None, false, false);
}
