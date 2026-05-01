namespace ViewGrid.Application.History;

/// <summary>
/// 履歴 UI の hover プレビュー（Phase 3）で「クリックしたらどっち方向にジャンプするか」を表す。
/// 表示色（Undo=赤系 / Redo=緑系）の選択や、ジャンプ範囲の意味づけに使う。
/// </summary>
public enum JumpDirection
{
    /// <summary>hover が現在位置と同じ、または hover 解除中。プレビュー範囲なし。</summary>
    None,

    /// <summary>hover が現在位置より古い（Index 小）→ クリックすると Undo が実行される。</summary>
    Undo,

    /// <summary>hover が現在位置より新しい（Index 大）→ クリックすると Redo が実行される。</summary>
    Redo,
}
