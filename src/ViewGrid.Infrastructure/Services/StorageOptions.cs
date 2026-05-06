namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// ストレージルートの設定。DI 経由で <see cref="FileSystemImageStorage"/> /
/// <see cref="SkiaThumbnailService"/> / <see cref="JsonAppSettingsService"/> に注入される。
/// </summary>
/// <remarks>
/// ワークスペース切替 (Phase 1) により 2 軸構成:
/// <list type="bullet">
/// <item><see cref="RootDirectory"/>: ワークスペース横断 (settings.json 等)</item>
/// <item><see cref="WorkspaceDirectory"/>: アクティブワークスペース固有 (DB / assets / thumbnails)</item>
/// </list>
/// テストでは両方を同じ TempDir に向けることが多い (旧 DataDirectory 単一と等価)。
/// </remarks>
public sealed class StorageOptions
{
    /// <summary>
    /// データルート（例: <c>%LocalAppData%\ViewGrid</c>）。
    /// ワークスペース横断で共有する設定ファイル (settings.json / workspaces.json / active.json)
    /// の置き場所。
    /// </summary>
    public required string RootDirectory { get; init; }

    /// <summary>
    /// 現在アクティブなワークスペースのデータディレクトリ
    /// （例: <c>%LocalAppData%\ViewGrid\workspaces\Default</c>）。
    /// DB / <see cref="AssetsSubdirectory"/> / <see cref="ThumbnailsSubdirectory"/> はここを基準に解決される。
    /// </summary>
    public required string WorkspaceDirectory { get; init; }

    /// <summary>アセット実体の格納ディレクトリ名（<see cref="WorkspaceDirectory"/> 配下）。</summary>
    public string AssetsSubdirectory { get; init; } = "assets";

    /// <summary>サムネイルの格納ディレクトリ名（<see cref="WorkspaceDirectory"/> 配下）。</summary>
    public string ThumbnailsSubdirectory { get; init; } = "thumbnails";
}
