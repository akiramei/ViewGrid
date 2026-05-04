using ErrorOr;

namespace ViewGrid.Application.History;

/// <summary>
/// アプリ全体で 1 本の Undo/Redo スタックを保持するサービス（singleton）。
/// 既存の自動保存 UseCase を <see cref="IUndoableCommand"/> でラップして
/// <see cref="ExecuteAsync"/> 経由で呼ぶことで、操作と同時に履歴へ積まれる。
/// 上限ステップ数を超えた古いエントリは破棄される。
/// </summary>
public interface IUndoRedoService
{
    /// <summary>取り消し可能な操作が積まれているか。</summary>
    bool CanUndo { get; }

    /// <summary>やり直し可能な操作が積まれているか。</summary>
    bool CanRedo { get; }

    /// <summary>次に Undo される操作の説明（メニュー文言用）。</summary>
    string? NextUndoDescription { get; }

    /// <summary>次に Redo される操作の説明（メニュー文言用）。</summary>
    string? NextRedoDescription { get; }

    /// <summary>スタック状態が変化したときに発火する。MainWindowViewModel 等が購読して CanExecute を更新する。</summary>
    event Action? StateChanged;

    /// <summary>
    /// Undo が成功してスタックが移動した直後に発火する。引数は取り消された Command。
    /// MainWindowViewModel がこれを購読し、<see cref="IUndoableCommand.AffectedGridId"/> を見て
    /// 必要に応じて配置タブのアクティブグリッドを当該グリッドへ切り替える。
    /// 失敗時（依存破綻による Clear 経路）や no-op（空スタック）では発火しない。
    /// </summary>
    event Action<IUndoableCommand>? Undone;

    /// <summary>
    /// Redo が成功してスタックが移動した直後に発火する。引数は再適用された Command。
    /// 用途・条件は <see cref="Undone"/> と同様。
    /// </summary>
    event Action<IUndoableCommand>? Redone;

    /// <summary>新規操作を実行して Undo スタックに積む。Redo スタックはクリアされる。</summary>
    Task<ErrorOr<Success>> ExecuteAsync(IUndoableCommand command, CancellationToken ct = default);

    /// <summary>直前の操作を取り消し、Redo スタックに移す。</summary>
    Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default);

    /// <summary>直前に Undo した操作を再適用し、Undo スタックに戻す。</summary>
    Task<ErrorOr<Success>> RedoAsync(CancellationToken ct = default);

    /// <summary>
    /// 履歴を全消去する。Undo の対象外操作（グリッド削除・アセット削除・コピー削除など
    /// cascade 削除を伴う操作）が走った後に、依存破綻を防ぐために起点側から呼ぶ。
    /// </summary>
    void Clear();

    /// <summary>
    /// 履歴の全エントリ。Index 昇順（先頭=最古、末尾=最新の操作）。
    /// <see cref="HistoryEntry.IsApplied"/>=<c>true</c> は適用済み（Undo 候補）、
    /// <c>false</c> は取消済み（Redo 候補）。
    /// 内部スタックの状態が変わるたびに新しいリストを生成する（呼び出し側がキャッシュするのは不可）。
    /// 履歴 UI（Flyout 等）はこのプロパティを <c>StateChanged</c> イベントの度に取得し直して表示する。
    /// </summary>
    IReadOnlyList<HistoryEntry> History { get; }

    /// <summary>
    /// 適用済みの最新エントリの Index。<c>-1</c> のとき何も適用されていない（全 Undo 状態 / 履歴空）。
    /// </summary>
    int CurrentIndex { get; }

    /// <summary>
    /// 指定 Index にジャンプする（任意ステップの一括 Undo / Redo）。
    /// <list type="bullet">
    ///   <item><c>targetIndex == CurrentIndex</c> なら no-op。</item>
    ///   <item><c>targetIndex &gt; CurrentIndex</c> なら <see cref="RedoAsync"/> を順に実行（取消済み → 適用済みへ進める）。</item>
    ///   <item><c>targetIndex &lt; CurrentIndex</c> なら <see cref="UndoAsync"/> を順に実行（適用済み → 取消済みへ戻す）。</item>
    /// </list>
    /// 範囲外（<c>&lt; -1</c> または <c>&gt;= History.Count</c>）は <see cref="ErrorType.Validation"/> を返す。
    /// 途中で個別の Undo/Redo が失敗（依存破綻等）したら、その時点で停止しエラーを返す
    /// （内部の Undo/Redo 失敗時の Clear 動作は維持される）。
    /// </summary>
    Task<ErrorOr<Success>> JumpToAsync(int targetIndex, CancellationToken ct = default);
}
