namespace ViewGrid.Application.History;

/// <summary>
/// 履歴 UI（メニュー / ツールバー / Flyout）に表示するための履歴エントリ。
/// <see cref="IUndoRedoService.History"/> から取得する。
/// </summary>
/// <param name="Description">操作の説明文。<see cref="IUndoableCommand.Description"/> をそのまま転記する。</param>
/// <param name="Index">履歴内の位置（0 始まり、最古=0、最新=<c>History.Count - 1</c>）。
/// <see cref="IUndoRedoService.JumpToAsync"/> の引数として使う。</param>
/// <param name="IsApplied">エントリが現在適用済み（=Undo 候補）なら <c>true</c>、
/// 取消済み（=Redo 候補）なら <c>false</c>。</param>
/// <param name="IsCurrent">このエントリが <see cref="IUndoRedoService.CurrentIndex"/> と一致するか
/// （描画ヒント用、現在位置インジケータの表示に使う）。</param>
public sealed record HistoryEntry(
    string Description,
    int Index,
    bool IsApplied,
    bool IsCurrent);
