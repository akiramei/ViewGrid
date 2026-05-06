namespace ViewGrid.Core.Settings;

/// <summary>
/// <c>workspaces.json</c> の 1 エントリ。 ワークスペースの内部識別名と表示名を保持する。
/// </summary>
/// <param name="Name">
/// 内部識別名。 ファイルシステムのディレクトリ名としても使うため、
/// 英数 + ハイフン + アンダースコアのみ許容 (FS 互換)。
/// </param>
/// <param name="DisplayName">
/// UI に表示する名称。 日本語可。 ユーザーがいつでもリネーム可能。
/// </param>
public sealed record WorkspaceManifest(string Name, string DisplayName);
