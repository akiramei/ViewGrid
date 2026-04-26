using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッドキャンバスを削除する。配置済みアイテムは DB の外部キー制約で cascade 削除される。
/// </summary>
public sealed class DeleteGridCanvasUseCase(IGridCanvasRepository repository)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(Guid id, CancellationToken ct = default) =>
        await repository.DeleteAsync(id, ct);
}
