namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// ストレージルートの設定。DI 経由で <see cref="FileSystemImageStorage"/> と
/// <see cref="SkiaThumbnailService"/> に注入される。
/// </summary>
public sealed class StorageOptions
{
    /// <summary>データルート（例: <c>%LocalAppData%\ViewGrid</c>）。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>アセット実体の格納ディレクトリ名（<see cref="DataDirectory"/> 配下）。</summary>
    public string AssetsSubdirectory { get; init; } = "assets";

    /// <summary>サムネイルの格納ディレクトリ名（<see cref="DataDirectory"/> 配下）。</summary>
    public string ThumbnailsSubdirectory { get; init; } = "thumbnails";
}
