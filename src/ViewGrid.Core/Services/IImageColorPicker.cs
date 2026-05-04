namespace ViewGrid.Core.Services;

/// <summary>
/// 画像の指定ピクセル位置から色を取得するサービス。AutoCrop の対象色を画像から
/// クリックで採取する UI で利用する。
/// </summary>
public interface IImageColorPicker
{
    /// <summary>
    /// 画像の (x, y) 位置のピクセル色を ARGB 32-bit (alpha が上位 8bit) で返す。
    /// 画像が読み込めない・座標が範囲外なら <c>null</c>。
    /// </summary>
    Task<uint?> PickColorAsync(
        string imageAbsolutePath,
        int x,
        int y,
        CancellationToken ct = default);
}
