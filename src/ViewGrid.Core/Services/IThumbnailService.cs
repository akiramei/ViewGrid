using ErrorOr;

namespace ViewGrid.Core.Services;

/// <summary>
/// アセットのサムネイル生成・取得を担う。生成したサムネイルは
/// <c>%LocalAppData%\ViewGrid\thumbnails\</c> 配下に保存される。
/// </summary>
public interface IThumbnailService
{
    /// <summary>長辺をこの画素数に収めてサムネイルを生成する。</summary>
    int MaxEdgePixels { get; }

    /// <summary>
    /// アセット実体からサムネイルを生成し、保存したサムネイルの相対パスを返す。
    /// 既に存在する場合はそれをそのまま返す。
    /// </summary>
    Task<ErrorOr<string>> GenerateAsync(string assetRelativePath, string fileHash, CancellationToken ct = default);

    /// <summary>
    /// 既存サムネを削除してから再生成する。 設定の <c>ThumbnailMaxEdgePixels</c> 変更を
    /// 既存アセットへ反映するための一括再生成 UseCase から呼ばれる。 既存ファイルが無い
    /// 場合は通常生成と同じ動作。 戻り値は新しく書き出した相対パス。
    /// </summary>
    Task<ErrorOr<string>> RegenerateAsync(string assetRelativePath, string fileHash, CancellationToken ct = default);

    /// <summary>サムネイルの絶対パスを解決する（存在しない場合は null）。</summary>
    string? TryResolveAbsolutePath(string fileHash);
}
