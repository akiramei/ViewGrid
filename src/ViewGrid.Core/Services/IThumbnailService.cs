using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>サムネイルの絶対パスを解決する（存在しない場合は null）。</summary>
    string? TryResolveAbsolutePath(string fileHash);
}
