using System.Collections.Immutable;
using ErrorOr;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// グリッドフィット（指定 placement の実描画矩形に列幅 / 行高を合わせる操作）の Undo/Redo ラッパ。
/// Execute では <see cref="FitGridWeightToPlacementUseCase"/> をそのまま呼び、
/// Undo では実行時点の旧 ColWeights / RowWeights を <see cref="UpdateGridWeightsUseCase"/> で書き戻す。
/// Redo は Execute の再実行（再計算）で対応する — 前提として、Undo/Redo 中は対象 placement と
/// 画像の状態が変化しないため、 fit の結果は決定論的に再現される。
/// </summary>
public sealed class FitGridWeightCommand : IUndoableCommand
{
    private readonly FitGridWeightToPlacementUseCase _fitUseCase;
    private readonly UpdateGridWeightsUseCase _updateWeights;
    private readonly Guid _gridId;
    private readonly Guid _placementId;
    private readonly FitAxis _axis;
    private readonly ImmutableArray<int> _beforeCol;
    private readonly ImmutableArray<int> _beforeRow;
    private readonly string _description;

    public FitGridWeightCommand(
        FitGridWeightToPlacementUseCase fitUseCase,
        UpdateGridWeightsUseCase updateWeights,
        Guid gridId,
        Guid placementId,
        FitAxis axis,
        ImmutableArray<int> beforeCol,
        ImmutableArray<int> beforeRow,
        string description)
    {
        _fitUseCase = fitUseCase;
        _updateWeights = updateWeights;
        _gridId = gridId;
        _placementId = placementId;
        _axis = axis;
        _beforeCol = beforeCol;
        _beforeRow = beforeRow;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _fitUseCase.ExecuteAsync(_placementId, _axis, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _updateWeights.ExecuteAsync(
            _gridId,
            (IReadOnlyList<int>)_beforeCol,
            (IReadOnlyList<int>)_beforeRow,
            ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }
}
