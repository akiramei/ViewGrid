using ErrorOr;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 指定したグリッドをアクティブに設定し、他を非アクティブにする。
/// </summary>
public sealed class SetActiveGridCanvasUseCase(IGridCanvasRepository repository)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(Guid id, CancellationToken ct = default) =>
        await repository.SetActiveAsync(id, ct);
}
