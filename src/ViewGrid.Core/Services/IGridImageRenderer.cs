using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Services;

/// <summary>
/// レンダリング 1 件分の入力。配置・コピー特性・元画像の絶対パスを束ねたプレーンな DTO。
/// </summary>
public sealed record PlacementRenderItem(
    GridPlacement Placement,
    ImageCopy Copy,
    string SourceImageAbsolutePath);

/// <summary>
/// グリッドと配置一覧から最終出力 PNG を合成する。
/// 出力サイズはグリッドの <see cref="GridCanvas.CanvasSize"/> に従う。
/// </summary>
public interface IGridImageRenderer
{
    /// <summary>
    /// 配置済み画像をピクセル精度で合成して PNG バイト列を返す。
    /// 描画順は <see cref="GridPlacement.PlacementOrder"/> の昇順（小さいものほど下）。
    /// </summary>
    /// <param name="trimMode">
    /// 出力時のトリミング指定。<see cref="TrimMode.None"/> はキャンバス全面（既定）、
    /// <see cref="TrimMode.OccupiedCells"/> は占有セル群の bbox で切り出し、
    /// <see cref="TrimMode.DrawnPixels"/> は α&gt;0 のピクセル走査で求めた bbox で切り出す。
    /// </param>
    Task<ErrorOr<byte[]>> RenderPngAsync(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        TrimMode trimMode = TrimMode.None,
        CancellationToken ct = default);
}
