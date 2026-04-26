using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;

namespace ViewGrid.Core.Services;

/// <summary>
/// 画像バイナリから幅・高さ・MIME タイプを取得する。
/// </summary>
public interface IImageProber
{
    Task<ErrorOr<ImageProbe>> ProbeAsync(Stream stream, CancellationToken ct = default);
}
