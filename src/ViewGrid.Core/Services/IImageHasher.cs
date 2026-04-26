using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ViewGrid.Core.Services;

/// <summary>
/// 画像バイナリのコンテンツハッシュを計算する。重複検出および保存パスの決定に用いる。
/// </summary>
public interface IImageHasher
{
    /// <summary>
    /// ストリームから SHA-256 を計算し、16 進小文字文字列（64 文字）を返す。
    /// 呼び出し元がストリームの所有権を保持する。
    /// </summary>
    Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default);
}
