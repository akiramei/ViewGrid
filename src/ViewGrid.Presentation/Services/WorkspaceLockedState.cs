namespace ViewGrid.Presentation.Services;

/// <summary>
/// 起動時に <see cref="ViewGrid.Infrastructure.Services.WorkspaceLock.TryAcquire(string)"/> の
/// 結果を <see cref="App"/> に渡すための DTO。 ロック失敗時はメインウィンドウの代わりに
/// <see cref="Views.WorkspaceLockedDialog"/> を表示してユーザーに通知する。
/// </summary>
/// <param name="IsLocked">ロック取得に失敗した (= 別プロセスが占有中) 場合 <c>true</c>。</param>
/// <param name="ActiveWorkspaceName">起動しようとしたワークスペース名 (失敗時の表示用)。</param>
/// <param name="OwnerProcessId">既存占有プロセスの PID (best-effort、 取得不能なら <c>null</c>)。</param>
internal sealed record WorkspaceLockedState(
    bool IsLocked,
    string ActiveWorkspaceName,
    int? OwnerProcessId);
