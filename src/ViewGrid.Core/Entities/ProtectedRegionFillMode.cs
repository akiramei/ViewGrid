namespace ViewGrid.Core.Entities;

/// <summary>
/// <see cref="ProtectedRegion"/> の元位置を親画像側でどう塗りつぶすかの選択肢。
/// </summary>
/// <remarks>
/// Phase 1 では <see cref="White"/> 一択。 <c>Transparent</c> / <c>Inpaint</c> 等は Phase 2 以降の
/// 拡張余地として「予約しない」 — 名前だけ先に置くと未実装なのに仕様があるように見えてしまうため、
/// 値は本当に必要なものだけを置く。
/// </remarks>
public enum ProtectedRegionFillMode
{
    /// <summary>親画像側の Region 元位置を白で塗りつぶす (Phase 1 既定)。</summary>
    White = 0,
}
