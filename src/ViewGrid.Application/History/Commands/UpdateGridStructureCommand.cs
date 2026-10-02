using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// グリッドの行・列の追加の Undo/Redo ラッパ。 変更前後の <see cref="GridStructure"/> を保持して
/// <see cref="UpdateGridStructureUseCase"/> を両方向に呼ぶ。 配置は動かさないので、 追加後に置いた配置は
/// 履歴上その後の操作として先に Undo され、 Undo で行・列を減らすときには範囲内に収まっている。
/// </summary>
public sealed class UpdateGridStructureCommand : IUndoableCommand
{
    private readonly UpdateGridStructureUseCase _useCase;
    private readonly Guid _gridId;
    private readonly GridStructure _before;
    private readonly GridStructure _after;

    public UpdateGridStructureCommand(
        UpdateGridStructureUseCase useCase,
        Guid gridId,
        GridStructure before,
        GridStructure after,
        string description)
    {
        _useCase = useCase;
        _gridId = gridId;
        _before = before;
        _after = after;
        Description = description;
    }

    public string Description { get; }

    public Guid? AffectedGridId => _gridId;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_gridId, _after, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_gridId, _before, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
