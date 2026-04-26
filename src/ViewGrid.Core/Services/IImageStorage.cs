using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ViewGrid.Core.Services;

/// <summary>
/// アプリ管理ストレージ（<c>%LocalAppData%\ViewGrid\assets\</c> 等）への画像ファイル入出力を担う。
/// 保存パスはコンテンツハッシュに基づいて決定される。
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// ハッシュと拡張子から保存先の相対パスを組み立てる（例: <c>assets/ab/abcdef...png</c>）。
    /// 実ファイルの有無は問わない。
    /// </summary>
    string BuildRelativePath(string fileHash, string extension);

    /// <summary>指定した相対パスのファイルが存在するか。</summary>
    bool Exists(string relativePath);

    /// <summary>
    /// 入力ストリームの内容を指定した相対パスに保存する。既存の場合は上書きしない。
    /// </summary>
    Task SaveAsync(Stream source, string relativePath, CancellationToken ct = default);

    /// <summary>相対パスを絶対パスに解決する。</summary>
    string ResolveAbsolutePath(string relativePath);

    /// <summary>相対パスのファイルを削除する（存在しない場合は何もしない）。</summary>
    void Delete(string relativePath);
}
