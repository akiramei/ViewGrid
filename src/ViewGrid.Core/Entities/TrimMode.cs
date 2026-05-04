namespace ViewGrid.Core.Entities;

/// <summary>
/// プレビュー / PNG 出力時の切り出し方法。<see cref="OutputMode"/> と直交する軸で、
/// 通常モード (<see cref="OutputMode.Normal"/>) では直接適用、写真ボードモード
/// (<see cref="OutputMode.PhotoBoard"/>) では合成後の画像に対して適用する。
/// 永続化はせず実行時の出力オプションとして用いる (既定 <see cref="None"/>)。
/// </summary>
public enum TrimMode
{
    /// <summary>
    /// グリッドの <see cref="GridCanvas.CanvasSize"/> 全面で出力 (既定)。
    /// 配置されていないセルは透過のまま残る。
    /// PhotoBoard モードでは拡張後のキャンバス全面で出力する。
    /// </summary>
    None = 0,

    /// <summary>
    /// 占有セル群のバウンディングボックスで切り出して出力。
    /// セル境界準拠なのでグリッドの構造が保たれ、配置済みセル内に Uniform 系の余白が
    /// あってもそのまま保持される (透過)。
    /// PhotoBoard モードでは元のセル位置に対応する領域 × 拡張倍率の bbox を使う。
    /// </summary>
    OccupiedCells = 1,

    /// <summary>
    /// レンダリング後の α&gt;0 ピクセルから bbox を計算して切り出して出力。
    /// Uniform 系で画像が小さく描画されている軸の余白も削除される。最小矩形を求めたい場合に使う。
    /// PhotoBoard モードでも同じく合成画像のピクセル走査で bbox を取る。
    /// </summary>
    DrawnPixels = 2,
}
