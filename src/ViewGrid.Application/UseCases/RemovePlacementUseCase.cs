using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

public sealed class RemovePlacementUseCase(IGridPlacementRepository placementRepository)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(Guid placementId, CancellationToken ct = default) =>
        await placementRepository.DeleteAsync(placementId, ct);
}
