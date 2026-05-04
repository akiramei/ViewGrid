namespace ViewGrid.Core.Entities;

/// <summary>
/// プレビュー / PNG 出力時の最上位モード。配置の合成方法を決める。
/// 切り出し方法 (<see cref="TrimMode"/>) とは直交軸で、両方を組み合わせて使う。
/// 永続化はせずセッション内のオプション扱い。
/// </summary>
public enum OutputMode
{
    /// <summary>
    /// 通常モード。グリッドのセル配置をそのままレンダリングする。
    /// クロップは <see cref="TrimMode"/> に従う。
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 写真ボード風モード。各配置を個別に切り出し、ポラロイド風フレーム + ドロップシャドウを
    /// 付けて、スタイル + 強度に応じたジッター / 回転 / 重なりで再合成する。
    /// シードは <see cref="GridCanvas"/>.Id 由来で再現性あり。
    /// </summary>
    PhotoBoard = 1,
}
