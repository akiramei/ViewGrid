namespace ViewGrid.Presentation.Services;

/// <summary>
/// 起動時のワークスペース解決の結果を <see cref="App"/> に渡すための DTO。
/// 表示名の取得と、 ワークスペースのデータが見つからなかったときの案内に使う。
/// </summary>
/// <param name="ActiveName">実際に開いたワークスペース名 (内部名)。</param>
/// <param name="MissingWorkspaceName">
/// 要求されたワークスペースのデータが見つからなかった場合のその名前。 <paramref name="ActiveName"/> と違えば
/// 別のワークスペースへ切り替えて開いた、 同じなら空で作り直した。 欠落が無ければ <c>null</c>。
/// </param>
internal sealed record WorkspaceStartupInfo(string ActiveName, string? MissingWorkspaceName);
