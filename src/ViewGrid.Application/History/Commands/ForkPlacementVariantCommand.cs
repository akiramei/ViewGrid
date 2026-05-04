using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 配置の参照バリアントを fork して独立化する操作の Undo/Redo ラッパ。
/// 初回 Execute は <see cref="ForkPlacementVariantUseCase"/> を呼び、結果として得られる
/// 新規 <see cref="ImageCopy"/> snapshot と元の CopyId を保持する。
/// <para>
/// Redo は同じ Id で <see cref="IImageCopyRepository.AddAsync"/> + Placement.CopyId 付け替え。
/// Undo は Placement.CopyId を元に戻し、新規 Copy を <see cref="IImageCopyRepository.DeleteAsync"/>
/// で削除する。
/// </para>
/// <para>
/// Undo→Redo 間に他の操作は走らない設計（<c>ExecuteAsync</c> が Redo スタックをクリア）のため、
/// 既存 <see cref="PlaceCommand"/> と同様 Validator の再実行は不要。
/// </para>
/// </summary>
public sealed class ForkPlacementVariantCommand : IUndoableCommand
{
    private readonly ForkPlacementVariantUseCase _forkUseCase;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly Guid _placementId;
    private readonly Guid _gridId;
    private readonly string _description;

    private ImageCopy? _newCopySnapshot;
    private Guid? _newCopyId;
    private Guid? _originalCopyId;
    private bool _firstExecute = true;

    public ForkPlacementVariantCommand(
        ForkPlacementVariantUseCase forkUseCase,
        IImageCopyRepository copyRepository,
        IGridPlacementRepository placementRepository,
        Guid placementId,
        Guid gridId,
        string description)
    {
        _forkUseCase = forkUseCase;
        _copyRepository = copyRepository;
        _placementRepository = placementRepository;
        _placementId = placementId;
        _gridId = gridId;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => _gridId;

    /// <summary>初回 Execute 後に確定する fork 結果の新 Copy Id。VM 側の選択遷移等に利用。</summary>
    public Guid? CreatedCopyId => _newCopyId;

    /// <summary>初回 Execute 後に確定する fork 元の Copy Id。Undo 用。</summary>
    public Guid? OriginalCopyId => _originalCopyId;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        if (_firstExecute)
        {
            var result = await _forkUseCase.ExecuteAsync(_placementId, ct).ConfigureAwait(false);
            if (result.IsError)
                return result.Errors;

            _newCopyId = result.Value.NewCopyId;
            _originalCopyId = result.Value.OriginalCopyId;
            _newCopySnapshot = result.Value.NewCopySnapshot;
            _firstExecute = false;
            return Result.Success;
        }

        // Redo: snapshot から同 Id で再 INSERT し、Placement.CopyId を付け替え直す。
        if (_newCopySnapshot is null || _newCopyId is not { } newId)
            return Error.Unexpected("ForkPlacementVariant.NoSnapshot",
                "Redo に必要な snapshot が初回 Execute で確定していません。");

        var addResult = await _copyRepository.AddAsync(_newCopySnapshot, ct).ConfigureAwait(false);
        if (addResult.IsError)
            return addResult.Errors;

        var placement = await _placementRepository.FindByIdAsync(_placementId, ct).ConfigureAwait(false);
        if (placement is null)
        {
            // 整合破綻 — 追加した Copy を削除して、上位の依存破綻ハンドラへ
            await _copyRepository.DeleteAsync(newId, ct).ConfigureAwait(false);
            return Error.NotFound("GridPlacement.NotFound",
                $"Redo 対象の Placement {_placementId} が見つかりません。");
        }

        var updated = ForkPlacementVariantUseCase.WithCopyId(placement, newId);
        var updResult = await _placementRepository.UpdateAsync(updated, ct).ConfigureAwait(false);
        if (updResult.IsError)
        {
            await _copyRepository.DeleteAsync(newId, ct).ConfigureAwait(false);
            return updResult.Errors;
        }
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        if (_newCopyId is not { } newId || _originalCopyId is not { } origId)
            return Result.Success; // 未実行 / no-op

        // Placement.CopyId を元の値へ戻す（init プロパティのため新インスタンスで Update）
        var placement = await _placementRepository.FindByIdAsync(_placementId, ct).ConfigureAwait(false);
        if (placement is null)
            return Error.NotFound("GridPlacement.NotFound",
                $"Undo 対象の Placement {_placementId} が見つかりません。");

        var reverted = ForkPlacementVariantUseCase.WithCopyId(placement, origId);
        var updResult = await _placementRepository.UpdateAsync(reverted, ct).ConfigureAwait(false);
        if (updResult.IsError)
            return updResult.Errors;

        // 新規バリアントを削除（元バリアントは触らない）
        var delResult = await _copyRepository.DeleteAsync(newId, ct).ConfigureAwait(false);
        return delResult.IsError ? delResult.Errors : Result.Success;
    }
}
