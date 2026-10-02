using ErrorOr;
using ViewGrid.Application.Preview;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// プレビューの自動更新用の描画 (<see cref="IIsolatedGridRenderer"/>) を、 DB に触れずに記録するスタブ。
/// 実装は専用スコープの DbContext で描画するが、 テストの in-memory SQLite は接続が 1 本なので
/// 並行アクセスを避けるために差し替える。
/// </summary>
internal sealed class StubIsolatedRenderer : IIsolatedGridRenderer
{
    public List<(Guid GridId, RenderOptions Options)> Calls { get; } = [];

    public int Count
    {
        get { lock (Calls) return Calls.Count; }
    }

    public Task<ErrorOr<byte[]>> RenderAsync(Guid gridId, RenderOptions options, CancellationToken ct = default)
    {
        lock (Calls) Calls.Add((gridId, options));
        return Task.FromResult<ErrorOr<byte[]>>(new byte[] { 1, 2, 3 });
    }
}
