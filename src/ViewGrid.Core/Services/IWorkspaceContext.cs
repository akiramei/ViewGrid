namespace ViewGrid.Core.Services;

/// <summary>
/// 起動中のワークスペース情報。 起動時に解決され、 プロセス内では不変。
/// 切替は <c>active.json</c> を書き換えてアプリ再起動する設計のため、
/// 動的更新は行わない。
/// </summary>
public interface IWorkspaceContext
{
    /// <summary>
    /// データルート（例: <c>%LocalAppData%\ViewGrid</c>）。
    /// ワークスペース横断で共有される設定 (settings.json / workspaces.json / active.json) の置き場所。
    /// </summary>
    string RootDirectory { get; }

    /// <summary>
    /// 現在アクティブなワークスペースのデータディレクトリ
    /// （例: <c>%LocalAppData%\ViewGrid\workspaces\Default</c>）。
    /// DB / アセット実体 / サムネはここに格納される。
    /// </summary>
    string WorkspaceDirectory { get; }

    /// <summary>
    /// 現在アクティブなワークスペース名（FS 互換、英数 + ハイフン）。
    /// </summary>
    string ActiveWorkspaceName { get; }
}
