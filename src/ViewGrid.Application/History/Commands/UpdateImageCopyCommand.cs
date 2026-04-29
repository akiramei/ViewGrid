using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.History.Commands;

/// <summary>
/// 論理コピーの特性（Rotation/Flip/Scaling/Trim/Align/Occupy/Name）編集の Undo/Redo ラッパ。
/// before / after の <see cref="UpdateImageCopyChanges"/>（全フィールドを明示的に保持）を
/// 構築時に受け取り、両方向に <see cref="UpdateImageCopyUseCase"/> を呼ぶ。
/// 共有コピーなので <see cref="AffectedGridId"/> は <c>null</c>（特定のグリッドに紐付かない）。
/// </summary>
public sealed class UpdateImageCopyCommand : IUndoableCommand
{
    private readonly UpdateImageCopyUseCase _useCase;
    private readonly Guid _copyId;
    private readonly UpdateImageCopyChanges _before;
    private readonly UpdateImageCopyChanges _after;
    private readonly string _description;

    public UpdateImageCopyCommand(
        UpdateImageCopyUseCase useCase,
        Guid copyId,
        UpdateImageCopyChanges before,
        UpdateImageCopyChanges after,
        string description)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        _useCase = useCase;
        _copyId = copyId;
        _before = before;
        _after = after;
        _description = description;
    }

    public string Description => _description;

    public Guid? AffectedGridId => null;

    public async Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_copyId, _after, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    public async Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
    {
        var result = await _useCase.ExecuteAsync(_copyId, _before, ct).ConfigureAwait(false);
        return result.IsError ? result.Errors : Result.Success;
    }

    /// <summary>
    /// 既存の <see cref="ImageCopy"/> から、UpdateImageCopyChanges に全フィールドを抽出する。
    /// <see cref="UpdateImageCopyChanges.ClearCopyName"/> は <c>source.CopyName is null</c> のときに <c>true</c> を立て、
    /// Undo で「null へ戻す」を明示できるようにする。
    /// </summary>
    public static UpdateImageCopyChanges SnapshotFrom(ImageCopy source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new UpdateImageCopyChanges
        {
            CopyName = source.CopyName,
            ClearCopyName = source.CopyName is null,
            Transform = source.Transform,
            ScalingMode = source.ScalingMode,
            TrimmingAnchor = source.TrimmingAnchor,
            Alignment = source.Alignment,
            OccupySize = source.OccupySize,
        };
    }
}
