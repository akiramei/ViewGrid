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
        var updated = new ImageCopy
        {
            Id = current.Id,
            AssetId = current.AssetId,
            CopyName = changes.CopyName ?? current.CopyName,
            Transform = changes.Transform ?? current.Transform,
            ScalingMode = changes.ScalingMode ?? current.ScalingMode,
            TrimmingAnchor = changes.TrimmingAnchor ?? current.TrimmingAnchor,
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
    public string? CopyName { get; init; }
    public ImageTransform? Transform { get; init; }
    public ScalingMode? ScalingMode { get; init; }
    public TrimmingAnchor? TrimmingAnchor { get; init; }
    public Alignment? Alignment { get; init; }
    public OccupySize? OccupySize { get; init; }
}
