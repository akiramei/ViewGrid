using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Preview;

/// <summary>
/// 他の保存・読込と同時に走ってもよい形でグリッドを描画する (開きっぱなしプレビューの自動更新用)。
/// </summary>
/// <remarks>
/// アプリの永続層 (DbContext) は 1 本を全 VM が共有しており、 同じコンテキストで 2 つの操作を並行に走らせると
/// EF Core が例外を投げる。 自動更新は編集の最中に裏で走るので、 保存や選択の再読込と重なる。
/// そのため専用のスコープ (= 専用の DbContext) で <see cref="RenderGridUseCase"/> を実行し、
/// 共有コンテキストには触れない。 読むのは確定済みの (保存済みの) 値だけ。
/// </remarks>
public interface IIsolatedGridRenderer
{
    Task<ErrorOr<byte[]>> RenderAsync(Guid gridId, RenderOptions options, CancellationToken ct = default);
}

/// <summary><see cref="IIsolatedGridRenderer"/> の標準実装。 描画のたびに新しい DI スコープを作る。</summary>
public sealed class ScopedGridRenderer(IServiceScopeFactory scopeFactory) : IIsolatedGridRenderer
{
    public async Task<ErrorOr<byte[]>> RenderAsync(Guid gridId, RenderOptions options, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<RenderGridUseCase>();
        return await useCase.ExecuteAsync(gridId, options, ct).ConfigureAwait(false);
    }
}
