namespace ViewGrid.Core.Entities;

/// <summary>
/// PhotoBoard モードのスタイルプリセット。各値は係数セット
/// (<see cref="PhotoBoardStyleCoefficients"/>) を引くキー。
/// 単一の chaos 値ではなく「回転量 / 重なり確率 / 拡張倍率 / フレーム濃度」など
/// 複数係数を独立に持つことで、同じ強度でもスタイル毎にハッキリ違う雰囲気を作れる。
/// </summary>
public enum PhotoBoardStyle
{
    /// <summary>
    /// ナチュラル: 軽い揺らぎ + 重なりなし。整然とした「写真アルバム」風。
    /// </summary>
    Natural = 0,

    /// <summary>
    /// ラフ: 中程度の回転 + たまの重なり。「ノートに貼った写真」風。
    /// </summary>
    Rough = 1,

    /// <summary>
    /// バラ撒き: 大胆な回転 + 頻繁な重なり + 大きめの拡張。
    /// 「机の上にぶちまけた写真」風。
    /// </summary>
    Scattered = 2,
}
