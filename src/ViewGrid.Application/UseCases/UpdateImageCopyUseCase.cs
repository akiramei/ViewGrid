using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 論理コピーの特性・変形・占有セル設定を更新する。
/// OccupySize 変更時は全関連 Placement についてグリッド境界 / 競合を検証し、
/// 1 件でも違反があれば永続化しない（View 再描画クラッシュの根本対策）。
/// </summary>
public sealed class UpdateImageCopyUseCase(
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository,
    IGridCanvasRepository gridRepository)
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

        var updatedOccupy = changes.OccupySize ?? current.OccupySize;

        // OccupySize 変更時は全関連 Placement に対してグリッド境界 / 競合を検証する。
        // 共有特性のため 1 つの ImageCopy が複数グリッドにまたがって配置されうる。
        if (changes.OccupySize is { } newSize && newSize != current.OccupySize)
        {
            var validation = await ValidateOccupySizeChangeAsync(copyId, newSize, ct);
            if (validation is { } error)
                return error;
        }

        var now = DateTimeOffset.UtcNow;
        // CopyName: 通常 null は「変更しない」を意味するが、ClearCopyName=true のときは
        // 「明示的に null で上書きする」を意味する（Undo で「無名 → 命名 → Undo（無名へ復帰）」を実現するため）。
        var updatedCopyName = changes.ClearCopyName
            ? null
            : (changes.CopyName ?? current.CopyName);
        // AutoCrop も同様の Optional 二段判定: ClearAutoCrop=true のときは null へ更新。
        // それ以外で AutoCrop が指定されていれば更新、null なら現状維持。
        var updatedAutoCrop = changes.ClearAutoCrop
            ? (AutoCropSettings?)null
            : (changes.AutoCrop ?? current.AutoCrop);
        // ManualCrop も同パターン。ClearManualCrop=true で明示 null 化。
        var updatedManualCrop = changes.ClearManualCrop
            ? (ManualCropFraction?)null
            : (changes.ManualCrop ?? current.ManualCrop);
        var updated = new ImageCopy
        {
            Id = current.Id,
            AssetId = current.AssetId,
            CopyName = updatedCopyName,
            Transform = changes.Transform ?? current.Transform,
            ScalingMode = changes.ScalingMode ?? current.ScalingMode,
            Alignment = changes.Alignment ?? current.Alignment,
            OccupySize = updatedOccupy,
            AutoCrop = updatedAutoCrop,
            ManualCrop = updatedManualCrop,
            CreatedAt = current.CreatedAt,
            UpdatedAt = now,
        };

        var result = await copyRepository.UpdateAsync(updated, ct);
        return result.IsError ? result.Errors : updated;
    }

    /// <summary>
    /// 新 OccupySize で全関連 Placement がグリッド内に収まり、他配置と競合しないかを検証する。
    /// 違反があれば対応する <see cref="Error"/> を返し、なければ <c>null</c>。
    /// GridPlacement は OccupySize を直接持たず ImageCopy.OccupySize を参照するため、
    /// 検証対象 copyId の Placement は「新 OccupySize」、他 Copy の Placement は
    /// 各 Copy の現状 OccupySize を取得して既存配置リストに含める。
    /// </summary>
    private async Task<Error?> ValidateOccupySizeChangeAsync(
        Guid copyId, OccupySize newSize, CancellationToken ct)
    {
        var affected = await placementRepository.FindByCopyIdAsync(copyId, ct);
        if (affected.Count == 0) return null;

        var byGrid = affected.GroupBy(p => p.GridId);
        foreach (var group in byGrid)
        {
            var grid = await gridRepository.FindByIdAsync(group.Key, ct);
            if (grid is null) continue;

            var allPlacementsInGrid = await placementRepository.FindByGridIdAsync(group.Key, ct);
            var existing = new List<ExistingPlacement>(allPlacementsInGrid.Count);
            var copySizeCache = new Dictionary<Guid, OccupySize>();
            foreach (var p in allPlacementsInGrid)
            {
                OccupySize size;
                if (p.CopyId == copyId)
                {
                    size = newSize;
                }
                else if (!copySizeCache.TryGetValue(p.CopyId, out size))
                {
                    var otherCopy = await copyRepository.FindByIdAsync(p.CopyId, ct);
                    if (otherCopy is null) continue;
                    size = otherCopy.OccupySize;
                    copySizeCache[p.CopyId] = size;
                }
                existing.Add(new ExistingPlacement(p.Id, p.Position, size));
            }

            foreach (var p in group)
            {
                var result = PlacementValidator.Validate(
                    newSize,
                    p.Position,
                    grid.GridRows,
                    grid.GridCols,
                    existing,
                    excludePlacementId: p.Id);
                if (!result.IsValid)
                {
                    return result.Reason switch
                    {
                        PlacementInvalidReason.OutOfBounds => Error.Validation(
                            "ImageCopy.OccupySizeOutOfBounds",
                            $"占有セル {newSize.Width}×{newSize.Height} は配置 ({p.Position.X},{p.Position.Y}) でグリッド「{grid.Name}」({grid.GridCols}×{grid.GridRows}) の範囲外になります。"),
                        PlacementInvalidReason.Conflict => Error.Validation(
                            "ImageCopy.OccupySizeConflict",
                            $"占有セル {newSize.Width}×{newSize.Height} は配置 ({p.Position.X},{p.Position.Y}) で他の配置と重なります。"),
                        _ => Error.Validation("ImageCopy.OccupySizeInvalid", "占有セルの変更が無効です。"),
                    };
                }
            }
        }
        return null;
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

    /// <summary>
    /// 単色余白の自動トリミング設定。<c>null</c> は通常「変更しない」を意味するが、
    /// <see cref="ClearAutoCrop"/> が <c>true</c> のときは「明示的に null へ更新（OFF へ）」を意味する。
    /// </summary>
    public AutoCropSettings? AutoCrop { get; init; }

    /// <summary>
    /// <see cref="AutoCrop"/> が <c>null</c> でも明示的に DB を <c>null</c> 更新するか。
    /// 既定 <c>false</c>（通常の更新フロー）。Undo/Redo で「OFF → ON → Undo（OFF に戻す）」の往復を実現するために必要。
    /// </summary>
    public bool ClearAutoCrop { get; init; }

    /// <summary>
    /// 任意矩形トリミング（手動）設定。<c>null</c> は通常「変更しない」を意味するが、
    /// <see cref="ClearManualCrop"/> が <c>true</c> のときは「明示的に null へ更新（OFF へ）」を意味する。
    /// AutoCrop と同時 ON でも ManualCrop が排他的に勝つ（Resolver で判定）。
    /// </summary>
    public ManualCropFraction? ManualCrop { get; init; }

    /// <summary>
    /// <see cref="ManualCrop"/> が <c>null</c> でも明示的に DB を <c>null</c> 更新するか。
    /// 既定 <c>false</c>。Undo/Redo で「矩形設定 → OFF → Undo（矩形設定に戻す）」の往復を実現するために必要。
    /// </summary>
    public bool ClearManualCrop { get; init; }
}
