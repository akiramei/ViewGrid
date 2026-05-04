using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// グリッド名を更新する。
/// </summary>
public sealed class RenameGridCanvasUseCase(IGridCanvasRepository repository)
{
    public async Task<ErrorOr<GridCanvas>> ExecuteAsync(Guid id, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return Error.Validation("GridCanvas.NameRequired", "グリッド名を入力してください。");

        var current = await repository.FindByIdAsync(id, ct);
        if (current is null)
            return Error.NotFound("GridCanvas.NotFound", $"GridCanvas {id} が見つかりません。");

        current.Name = newName.Trim();
        current.UpdatedAt = DateTimeOffset.UtcNow;

        var update = await repository.UpdateAsync(current, ct);
        if (update.IsError)
            return update.Errors;
        return current;
    }
}
