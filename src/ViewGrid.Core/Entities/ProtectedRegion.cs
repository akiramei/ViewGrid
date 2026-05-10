namespace ViewGrid.Core.Entities;

/// <summary>
/// セル内画像の一部を独立したアセットとして分離した 「保護領域」。
/// 元画像 (Crop / Transform 適用前) の <see cref="Rect"/> から切り出され、 セル内の任意位置
/// (<see cref="OffsetXPx"/> / <see cref="OffsetYPx"/>) に再配置される。 親の共有特性のうち
/// スケーリングだけ追従し、 アライメントは無視する。 region 自身の <see cref="Rotation"/> /
/// <see cref="FlipX"/> / <see cref="FlipY"/> は asset 描画にのみ作用する (親側塗りには影響しない)。
/// </summary>
/// <remarks>
/// <para>処理フロー:</para>
/// <list type="number">
///   <item>元画像座標系 0–1 比率の <see cref="Rect"/> から asset を切り出し</item>
///   <item>親の source→cell スケール係数で asset をリサイズ (アライメント無視)</item>
///   <item>region 自身の <see cref="Rotation"/> / <see cref="FlipX"/> / <see cref="FlipY"/> を適用
///     (90度刻み、 0/180 は cell-local W/H 不変、 90/270 は axis swap)</item>
///   <item>cell-local pixel <c>(<see cref="OffsetXPx"/>, <see cref="OffsetYPx"/>)</c> に
///     回転後 bbox の左上揃えで配置、 cell 矩形で clip</item>
///   <item>親側は <see cref="Rect"/> ∩ effective Crop の可視部分を <see cref="FillMode"/>
///     (+ <see cref="FillColor"/>) で塗る (region の <see cref="Rotation"/> / Flip は無視)</item>
///   <item>PhotoBoard 出力時はセル合成後の結果に対してばらつき (回転 / オフセット) を適用するため、
///     region は親画像と一緒に揺れる</item>
/// </list>
/// <para>通常モード / PhotoBoard モードの両方で有効。</para>
/// </remarks>
public sealed class ProtectedRegion
{
    public required Guid Id { get; init; }

    /// <summary>所属する <see cref="ImageCopy"/> の Id (FK)。</summary>
    public required Guid ImageCopyId { get; init; }

    /// <summary>元画像座標系の bbox (0–1 比率)。 Crop / Transform 適用前の座標。</summary>
    public required RegionRectFraction Rect { get; init; }

    /// <summary>親画像側の元位置の塗りつぶし方法。</summary>
    public required ProtectedRegionFillMode FillMode { get; init; }

    /// <summary>
    /// <see cref="FillMode"/> が <see cref="ProtectedRegionFillMode.Custom"/> のときに使う ARGB 色。
    /// それ以外のプリセット (White / Black / Transparent) では <c>null</c> 推奨 (renderer 側で無視)。
    /// </summary>
    public uint? FillColor { get; init; }

    /// <summary>
    /// セル内 pixel 単位の X オフセット (左上基準)。 既定 0。 負値も許可 (左にはみ出した分は cell 矩形で clip される)。
    /// </summary>
    public int OffsetXPx { get; init; }

    /// <summary>
    /// セル内 pixel 単位の Y オフセット (左上基準)。 既定 0。 負値も許可 (上にはみ出した分は cell 矩形で clip される)。
    /// </summary>
    public int OffsetYPx { get; init; }

    /// <summary>
    /// region 自身の回転 (90度刻み、 時計回り)。 既定 <see cref="Core.Entities.Rotation.None"/>。
    /// 親 placement の回転とは独立に作用し、 asset 描画にのみ影響する (親側塗りには無関係)。
    /// </summary>
    public Rotation Rotation { get; init; } = Rotation.None;

    /// <summary>region 自身の水平反転 (asset 描画時に左右ミラー)。 既定 <c>false</c>。</summary>
    public bool FlipX { get; init; }

    /// <summary>region 自身の垂直反転 (asset 描画時に上下ミラー)。 既定 <c>false</c>。</summary>
    public bool FlipY { get; init; }

    /// <summary>
    /// 同一 <see cref="ImageCopy"/> 内での描画順 (小さい順に描画)。
    /// </summary>
    public required int SortOrder { get; init; }
}
