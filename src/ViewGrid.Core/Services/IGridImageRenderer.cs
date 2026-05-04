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
    /// <param name="grid">出力対象のグリッド。</param>
    /// <param name="items">配置と元画像のペア列。</param>
    /// <param name="options">
    /// 出力モード + PhotoBoard 固有パラメータ。<see cref="RenderOptions.Default"/>
    /// は <see cref="TrimMode.None"/> 相当。
    /// </param>
    /// <param name="ct">キャンセルトークン。</param>
    Task<ErrorOr<byte[]>> RenderPngAsync(
        GridCanvas grid,
        IReadOnlyList<PlacementRenderItem> items,
        RenderOptions options,
        CancellationToken ct = default);
}
