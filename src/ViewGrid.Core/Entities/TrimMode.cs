namespace ViewGrid.Core.Entities;

/// <summary>
/// PNG 出力時の合成・切り出しモード。プレビューと PNG 出力の両方で同じ設定が適用される。
/// 永続化はせず実行時の出力オプションとして用いる。
///
/// <para>
/// 既存 3 値 (<see cref="None"/> / <see cref="OccupiedCells"/> / <see cref="DrawnPixels"/>)
/// は「外周を矩形クロップする」性質で共通だが、<see cref="PhotoBoard"/> は配置を再合成する
/// 性質的に別系統のモード。
/// </para>
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

    /// <summary>
    /// 写真ボード風出力モード。各配置を個別に切り出し、フレーム / ドロップシャドウを付け、
    /// 「整列 ↔ 散らかし」軸 (<see cref="RenderOptions.PhotoBoardChaos"/>) に応じて
    /// ジッター + 回転を適用して再合成する。<c>chaos=0</c> では <see cref="None"/> と
    /// 同一出力 (フレーム / シャドウ / ジッター / 回転すべて無効)、<c>chaos=1</c> で
    /// 最大効果。シードは <see cref="GridCanvas.Id"/> 由来で再現性あり。
    /// </summary>
    PhotoBoard = 3,
}
