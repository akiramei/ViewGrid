using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Localization;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// グリッドの構造編集 (列・行の重み更新 / ロック / 配置に合わせた重み自動調整) を司る VM。
/// Phase 4 で <see cref="GridWorkspaceViewModel"/> から抽出。
/// <para>
/// CurrentGrid の参照と StatusMessage 書込は親 (<see cref="IGridStructureEditorContext"/>) を経由する。
/// 重み / ロックの永続化成功時は <see cref="IGridStructureEditorContext.NotifyCurrentGridChanged"/> で
/// 親に通知して View binding を再評価させる (GridCanvasView 側の Rebuild トリガー)。
/// </para>
/// </summary>
public sealed partial class GridStructureEditorViewModel : ViewModelBase
{
    private readonly IGridCanvasRepository _gridRepository;
    private readonly UpdateGridWeightsUseCase _updateWeightsUseCase;
    private readonly UpdateGridLocksUseCase _updateLocksUseCase;
    private readonly FitGridWeightToPlacementUseCase _fitWeightUseCase;
    private readonly IUndoRedoService _history;
    private readonly ILocalizationService _loc;
    private IGridStructureEditorContext? _context;

    /// <summary>
    /// <see cref="AttachContext"/> で attach された親 (<see cref="GridWorkspaceViewModel"/>) へのアクセサ。
    /// 未 attach のままメソッドが呼ばれた場合は明確な例外を出して構築順の誤りを早期検出する。
    /// </summary>
    private IGridStructureEditorContext Context => _context
        ?? throw new InvalidOperationException(
            $"{nameof(GridStructureEditorViewModel)}.{nameof(AttachContext)} must be called before using this VM.");

    public GridStructureEditorViewModel(
        IGridCanvasRepository gridRepository,
        UpdateGridWeightsUseCase updateWeightsUseCase,
        UpdateGridLocksUseCase updateLocksUseCase,
        FitGridWeightToPlacementUseCase fitWeightUseCase,
        IUndoRedoService history,
        ILocalizationService loc)
    {
        _gridRepository = gridRepository;
        _updateWeightsUseCase = updateWeightsUseCase;
        _updateLocksUseCase = updateLocksUseCase;
        _fitWeightUseCase = fitWeightUseCase;
        _history = history;
        _loc = loc;
    }

    /// <summary>
    /// 親 (<see cref="GridWorkspaceViewModel"/>) を attach する 2-phase 初期化。
    /// 詳細は <see cref="GridOutputViewModel.AttachContext"/> 参照。
    /// </summary>
    public void AttachContext(IGridStructureEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_context is not null)
            throw new InvalidOperationException($"{nameof(GridStructureEditorViewModel)} is already attached.");
        _context = context;
    }

    /// <summary>
    /// 境界ドラッグでの重み更新を保存する。 <paramref name="colWeights"/> または
    /// <paramref name="rowWeights"/> のどちらかが null (変更なし) でも構わない。
    /// 成功時は <see cref="IGridStructureEditorContext.CurrentGrid"/> の重みを更新して View にリビルドさせる。
    /// </summary>
    public async Task<bool> ApplyGridWeightsAsync(
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CancellationToken ct = default)
    {
        var grid = Context.CurrentGrid;
        if (grid is null) return false;

        var beforeCol = grid.ColWeights;
        var beforeRow = grid.RowWeights;
        var afterCol = BuildAfterWeights(beforeCol, colWeights);
        var afterRow = BuildAfterWeights(beforeRow, rowWeights);

        var colChanged = !afterCol.SequenceEqual(beforeCol);
        var rowChanged = !afterRow.SequenceEqual(beforeRow);
        if (!colChanged && !rowChanged) return true; // 値変化なし — 履歴に積まない

        var description = _loc.Format(ResolveWeightsChangeFormatKey(colChanged, rowChanged), grid.Name);
        var command = new UpdateGridWeightsCommand(
            _updateWeightsUseCase, grid.GridId, beforeCol, beforeRow, afterCol, afterRow, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            Context.StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // 永続化された最新値を再取得して VM に反映
        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is not null)
        {
            grid.ColWeights = reloaded.ColWeights;
            grid.RowWeights = reloaded.RowWeights;
            Context.NotifyCurrentGridChanged();
        }
        Context.StatusMessage = _loc["Status_GridWeightsUpdated"];
        return true;
    }

    /// <summary>
    /// 重み更新の after 配列を構築する。 <paramref name="after"/> が <c>null</c>
    /// (= その軸は変更なし) なら <paramref name="before"/> をそのまま返す。
    /// </summary>
    private static ImmutableArray<int> BuildAfterWeights(ImmutableArray<int> before, IReadOnlyList<int>? after) =>
        after is null ? before : [.. after];

    /// <summary>
    /// 履歴 description 用の resx format key を決める。 両軸変化時は「比率」、 片軸のみ変化時は「列幅」/「行高」 系。
    /// </summary>
    private static string ResolveWeightsChangeFormatKey(bool colChanged, bool rowChanged) =>
        colChanged && rowChanged ? "History_WeightsChangedRatiosFmt"
            : (colChanged ? "History_WeightsChangedColFmt" : "History_WeightsChangedRowFmt");

    /// <summary>
    /// 指定 placement の実描画矩形に合わせて、 占有列幅または行高を縮める。
    /// 余白は隣接列/行に分配 (端列/端行で隣接がない側の余白は破棄)。
    /// 成功時は最新の重みを CurrentGrid に反映し、 View を再構築させる。
    /// 操作は <see cref="FitGridWeightCommand"/> でラップして履歴に積むため、 Undo で旧重みに戻り、
    /// Redo で再計算される。 fit が no-op だった場合も command は履歴に積まれる
    /// (空の Undo エントリになるが、 redo スタックの stale snapshot を確実に破棄するため)。
    /// </summary>
    public async Task<bool> FitGridWeightAsync(
        Guid placementId, FitAxis axis, CancellationToken ct = default)
    {
        var grid = Context.CurrentGrid;
        if (grid is null) return false;

        var beforeCol = grid.ColWeights;
        var beforeRow = grid.RowWeights;

        var description = _loc.Format(
            axis == FitAxis.Column ? "History_FitGridColFmt" : "History_FitGridRowFmt",
            grid.Name);
        var command = new FitGridWeightCommand(
            _fitWeightUseCase, _updateWeightsUseCase,
            grid.GridId, placementId, axis,
            beforeCol, beforeRow,
            description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            Context.StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // 重みが変わった可能性があるので、 グリッドを再読込して反映
        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is null) return false;

        var changed =
            !reloaded.ColWeights.SequenceEqual(beforeCol) ||
            !reloaded.RowWeights.SequenceEqual(beforeRow);

        grid.ColWeights = reloaded.ColWeights;
        grid.RowWeights = reloaded.RowWeights;
        Context.NotifyCurrentGridChanged();

        Context.StatusMessage = changed
            ? _loc[axis == FitAxis.Column ? "Status_FitColumnDone" : "Status_FitRowDone"]
            : _loc["Status_FitNoTarget"];
        return true;
    }

    /// <summary>
    /// 指定列のロック状態を反転する (true ↔ false)。
    /// 成功時は <see cref="IGridStructureEditorContext.CurrentGrid"/> の ColLocked も更新して
    /// View を再構築させる。 実体は <see cref="ToggleAxisLockAsync"/> に委譲。
    /// </summary>
    public Task<bool> ToggleColLockAsync(int colIndex, CancellationToken ct = default) =>
        ToggleAxisLockAsync(FitAxis.Column, colIndex, ct);

    /// <summary>指定行のロック状態を反転する。 実体は <see cref="ToggleAxisLockAsync"/> に委譲。</summary>
    public Task<bool> ToggleRowLockAsync(int rowIndex, CancellationToken ct = default) =>
        ToggleAxisLockAsync(FitAxis.Row, rowIndex, ct);

    /// <summary>
    /// 列 / 行のロック状態を反転する共通実装。 列・行の対称性を <see cref="FitAxis"/> 引数で吸収し、
    /// Toggle{Col,Row}LockAsync 双子メソッドの重複を排した。 axis 側の locked 配列を反転し、
    /// もう一方の axis の値はそのまま <see cref="UpdateGridLocksCommand"/> に渡す。
    /// </summary>
    private async Task<bool> ToggleAxisLockAsync(FitAxis axis, int index, CancellationToken ct)
    {
        var grid = Context.CurrentGrid;
        if (grid is null) return false;

        var axisCount = axis == FitAxis.Column ? grid.Cols : grid.Rows;
        if (index < 0 || index >= axisCount) return false;

        // axis 側 (反転対象) ともう一方 (other、 そのまま渡す) を正規化して取得。
        var beforeAxis = NormalizeLocks(axis == FitAxis.Column ? grid.ColLocked : grid.RowLocked, axisCount);
        var afterAxis = beforeAxis.SetItem(index, !beforeAxis[index]);
        var otherCount = axis == FitAxis.Column ? grid.Rows : grid.Cols;
        var beforeOther = NormalizeLocks(axis == FitAxis.Column ? grid.RowLocked : grid.ColLocked, otherCount);

        // UpdateGridLocksCommand は (beforeCol, beforeRow, afterCol, afterRow) を取るので、
        // axis に応じて引数の Col/Row 側を組み立てる。
        var (commandBeforeCol, commandBeforeRow, commandAfterCol, commandAfterRow) = axis == FitAxis.Column
            ? (beforeAxis, beforeOther, afterAxis, beforeOther)
            : (beforeOther, beforeAxis, beforeOther, afterAxis);

        var isLocked = afterAxis[index];
        var formatKey = (axis, isLocked) switch
        {
            (FitAxis.Column, true) => "History_ColLockedFmt",
            (FitAxis.Column, false) => "History_ColUnlockedFmt",
            (FitAxis.Row, true) => "History_RowLockedFmt",
            _ => "History_RowUnlockedFmt",
        };
        var description = _loc.Format(formatKey, index, grid.Name);

        var command = new UpdateGridLocksCommand(
            _updateLocksUseCase, grid.GridId,
            commandBeforeCol, commandBeforeRow, commandAfterCol, commandAfterRow,
            description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            Context.StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // 永続化後の最新値で grid の axis 側だけを更新 (other は変えていない)。
        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is not null)
        {
            if (axis == FitAxis.Column) grid.ColLocked = reloaded.ColLocked;
            else grid.RowLocked = reloaded.RowLocked;
            Context.NotifyCurrentGridChanged();
        }

        var statusKeyOn = axis == FitAxis.Column ? "Status_ColLockedFmt" : "Status_RowLockedFmt";
        var statusKeyOff = axis == FitAxis.Column ? "Status_ColUnlockedFmt" : "Status_RowUnlockedFmt";
        Context.StatusMessage = _loc.Format(afterAxis[index] ? statusKeyOn : statusKeyOff, index);
        return true;
    }

    /// <summary>
    /// 期待長と一致する <see cref="ImmutableArray{T}"/> をそのまま返す。 不一致 (旧データ等で
    /// length が ColLocked.Length != Cols 等) の場合は全 false の配列で正規化する。
    /// </summary>
    private static ImmutableArray<bool> NormalizeLocks(ImmutableArray<bool> source, int expectedLength) =>
        source.Length == expectedLength
            ? source
            : [.. Enumerable.Range(0, expectedLength).Select(_ => false)];
}

/// <summary>
/// <see cref="GridStructureEditorViewModel"/> が親 (<see cref="GridWorkspaceViewModel"/>) から借りるコンテキスト。
/// CurrentGrid の参照 + StatusMessage 書込 + 「CurrentGrid 内部 (重み / ロック) が変わった」 通知の 3 つを露出。
/// </summary>
public interface IGridStructureEditorContext
{
    /// <summary>現在ワークスペースが表示しているグリッド。 未選択時は <c>null</c>。</summary>
    GridCanvasItemViewModel? CurrentGrid { get; }

    /// <summary>グローバルステータスバー表示用メッセージ。 ローカライズ済み文字列または <c>null</c>。</summary>
    string? StatusMessage { get; set; }

    /// <summary>
    /// CurrentGrid 内部の状態 (ColWeights / RowWeights / ColLocked / RowLocked) を直接変更した後に呼び、
    /// 親 VM が <c>OnPropertyChanged(nameof(CurrentGrid))</c> 相当の通知を発行できるようにする。
    /// GridCanvasView は CurrentGrid 経由で重み配列を読むので、 これがないと View が rebuild されない。
    /// </summary>
    void NotifyCurrentGridChanged();
}
