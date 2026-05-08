namespace ViewGrid.Core.Entities;

/// <summary>
/// PhotoBoard 出力時に親写真の回転を受けず、 最終キャンバス水平で再描画される
/// 「保護領域」。 セリフ・注釈・人物の顔など、 親画像の傾きに引っ張られて
/// 読みにくくなる要素を元画像座標系の Rect で固定する。
/// </summary>
/// <remarks>
/// <para>処理フロー (Phase 1):</para>
/// <list type="number">
///   <item>元画像座標系の <see cref="Rect"/> を保持</item>
///   <item>RenderPhotoBoard で各 placement の cell-bounded 中間画像を生成</item>
///   <item>ManualCrop / AutoCrop で決定した実効 Crop と交差判定</item>
///   <item>交差すれば、 親画像側の該当領域を <see cref="FillMode"/> で塗りつぶす</item>
///   <item>同じ領域を Overlay として切り出し、 PhotoBoard のランダム回転を打ち消した
///     キャンバス水平向きで最終キャンバスに合成</item>
/// </list>
/// <para>「KeepUpright」 の意味は 「<see cref="ImageCopy.Transform"/> で指定された 90 度回転等は
/// 尊重し、 PhotoBoard が遊びで傾けたランダム回転だけを打ち消す」。</para>
/// <para>Phase 1 では <see cref="Rect"/> のみ・<see cref="FillMode"/> は
/// <see cref="ProtectedRegionFillMode.White"/> 一択・PhotoBoard 出力のみ対応。
/// Polygon / Snap90 / FollowParent / OCR / Inpaint は Phase 2 以降。</para>
/// </remarks>
public sealed class ProtectedRegion
{
    public required Guid Id { get; init; }

    /// <summary>所属する <see cref="ImageCopy"/> の Id (FK)。</summary>
    public required Guid ImageCopyId { get; init; }

    /// <summary>元画像座標系の bbox (0–1 比率)。</summary>
    public required RegionRectFraction Rect { get; init; }

    /// <summary>親画像側の元位置の塗りつぶし方法。</summary>
    public required ProtectedRegionFillMode FillMode { get; init; }

    /// <summary>
    /// 同一 <see cref="ImageCopy"/> 内での描画順 (小さい順に描画)。
    /// Phase 1 では追加順 = sort_order。 並べ替え UI は Phase 2 で検討。
    /// </summary>
    public required int SortOrder { get; init; }
}
