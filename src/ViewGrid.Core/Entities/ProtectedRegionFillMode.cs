namespace ViewGrid.Core.Entities;

/// <summary>
/// <see cref="ProtectedRegion"/> の元位置を親画像側でどう塗りつぶすかの選択肢。
/// </summary>
/// <remarks>
/// <see cref="Custom"/> は <see cref="ProtectedRegion.FillColor"/> (ARGB) と組で使う。
/// その他のプリセットでは FillColor は無視される。
/// </remarks>
public enum ProtectedRegionFillMode
{
    /// <summary>白 (0xFFFFFFFF) で塗りつぶす。 既定値。</summary>
    White = 0,

    /// <summary>黒 (0xFF000000) で塗りつぶす。</summary>
    Black = 1,

    /// <summary>透明 (アルファ 0) で塗りつぶす = 親画像側に穴をあける。</summary>
    Transparent = 2,

    /// <summary><see cref="ProtectedRegion.FillColor"/> で指定された ARGB 色で塗りつぶす。
    /// 通常はエディタ上で元画像をクリックして取得した色 (eyedropper)。</summary>
    Custom = 3,
}
