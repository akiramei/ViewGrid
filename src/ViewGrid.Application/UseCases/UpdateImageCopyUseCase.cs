using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 論理コピーの特性・変形・占有セル設定を更新する。
/// </summary>
public sealed class UpdateImageCopyUseCase(IImageCopyRepository copyRepository)
{
    public async Task<ErrorOr<ImageCopy>> ExecuteAsync(
        Guid copyId,
        UpdateImageCopyChanges changes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var current = await copyRepository.FindByIdAsync(copyId, ct);
        if (current is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {copyId} が見つかりません。");

        var now = DateTimeOffset.UtcNow;
        // CopyName: 通常 null は「変更しない」を意味するが、ClearCopyName=true のときは
        // 「明示的に null で上書きする」を意味する（Undo で「無名 → 命名 → Undo（無名へ復帰）」を実現するため）。
        var updatedCopyName = changes.ClearCopyName
            ? null
            : (changes.CopyName ?? current.CopyName);
        var updated = new ImageCopy
        {
            Id = current.Id,
            AssetId = current.AssetId,
            CopyName = updatedCopyName,
            Transform = changes.Transform ?? current.Transform,
            ScalingMode = changes.ScalingMode ?? current.ScalingMode,
            Alignment = changes.Alignment ?? current.Alignment,
            OccupySize = changes.OccupySize ?? current.OccupySize,
            CreatedAt = current.CreatedAt,
            UpdatedAt = now,
        };

        var result = await copyRepository.UpdateAsync(updated, ct);
        return result.IsError ? result.Errors : updated;
    }
}

public sealed record UpdateImageCopyChanges
{
    /// <summary>
    /// 新しい CopyName。<c>null</c> は通常「変更しない」を意味するが、
    /// <see cref="ClearCopyName"/> が <c>true</c> のときは「明示的に null へ更新する」を意味する。
    /// </summary>
    public string? CopyName { get; init; }

    /// <summary>
    /// <see cref="CopyName"/> が <c>null</c> でも明示的に DB を <c>null</c> 更新するか。
    /// 既定 <c>false</c>（通常の更新フロー、null は「変更しない」）。
    /// Undo/Redo で「無名 → 命名 → Undo（無名に戻す）」の往復を実現するために必要。
    /// </summary>
    public bool ClearCopyName { get; init; }

    public ImageTransform? Transform { get; init; }
    public ScalingMode? ScalingMode { get; init; }
    public Alignment? Alignment { get; init; }
    public OccupySize? OccupySize { get; init; }
}
