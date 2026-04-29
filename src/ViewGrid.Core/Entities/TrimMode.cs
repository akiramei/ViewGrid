namespace ViewGrid.Core.Entities;

/// <summary>
/// PNG 出力時に未使用領域をどう扱うかの切替。プレビューと PNG 出力の両方で同じ設定が
/// 適用される。永続化はせず実行時の出力オプションとして用いる。
/// </summary>
public enum TrimMode
{
    /// <summary>
    /// グリッドの <see cref="GridCanvas.CanvasSize"/> 全面で出力（既定）。
    /// 配置されていないセルは透過のまま残る。
    /// </summary>
    None = 0,

    /// <summary>
    /// 占有セル群のバウンディングボックスで切り出して出力。
    /// セル境界準拠なのでグリッドの構造が保たれ、配置済みセル内に Uniform 系の余白が
    /// あってもそのまま保持される（透過）。
    /// </summary>
    OccupiedCells = 1,

    /// <summary>
    /// レンダリング後の α&gt;0 ピクセルから bbox を計算して切り出して出力。
    /// Uniform 系で画像が小さく描画されている軸の余白も削除される。最小矩形を求めたい場合に使う。
    /// </summary>
    DrawnPixels = 2,
}
