using System.Collections.Immutable;
using ErrorOr;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 既存の <see cref="GridPlacement"/> の参照する <see cref="ImageCopy"/> を fork して
/// その配置だけ独立したバリアントに分岐する。元の <see cref="ImageCopy"/> の全特性
/// （Transform / ScalingMode / Alignment / OccupySize / AutoCrop / ManualCrop / Regions）を
/// コピーした新規バリアントを作成し、当該 Placement の <see cref="GridPlacement.CopyId"/>
/// を新しい Id へ付け替える。
/// <para>
/// 命名規則: 元バリアントの <see cref="ImageCopy.CopyName"/> に「(派生)」を付加。
/// 元名が無名の場合は「バリアント (派生)」になる。連続して fork した場合の
/// 「(派生) (派生)」は許容（Lightroom の "(Copy)" 等と同じ慣習）。
/// </para>
/// <para>
/// Placement 自身の Id / Position / PixelOffset / PlacementOrder / GridId は変えず、
/// CopyId のみ更新する。Placement の Update が失敗した場合は新規 Copy を削除して
/// クリーンアップする（ロールバック）。
/// </para>
/// </summary>
public sealed class ForkPlacementVariantUseCase(
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository)
{
    public async Task<ErrorOr<ForkPlacementVariantResult>> ExecuteAsync(
        Guid placementId,
        CancellationToken ct = default)
    {
        var placement = await placementRepository.FindByIdAsync(placementId, ct);
        if (placement is null)
            return Error.NotFound("GridPlacement.NotFound", $"GridPlacement {placementId} が見つかりません。");

        var sourceCopy = await copyRepository.FindByIdAsync(placement.CopyId, ct);
        if (sourceCopy is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {placement.CopyId} が見つかりません。");

        var now = DateTimeOffset.UtcNow;
        var newCopyId = Guid.NewGuid();
        var newCopyName = MakeForkName(sourceCopy.CopyName);
        var newCopy = CloneWithNewId(sourceCopy, newCopyId, newCopyName, now);

        var addResult = await copyRepository.AddAsync(newCopy, ct);
        if (addResult.IsError)
            return addResult.Errors;

        var updated = WithCopyId(placement, newCopyId);
        var updResult = await placementRepository.UpdateAsync(updated, ct);
        if (updResult.IsError)
        {
            // ロールバック: 新規 Copy を削除して整合を保つ。
            await copyRepository.DeleteAsync(newCopyId, ct);
            return updResult.Errors;
        }

        return new ForkPlacementVariantResult(
            NewCopyId: newCopyId,
            OriginalCopyId: placement.CopyId,
            NewCopySnapshot: newCopy);
    }

    /// <summary>
    /// 元バリアントの全特性を保持したまま、Id / CopyName / 作成日時だけ差し替えた新規 ImageCopy を生成する。
    /// init-only プロパティを含むため <c>with</c> 式は使えず、明示コンストラクトする。
    /// 保護領域 (<see cref="ImageCopy.Regions"/>) も複製するが、各 Region は新しい Id と新バリアントの
    /// <see cref="ProtectedRegion.ImageCopyId"/> を持つ独立インスタンスにする (IO-3 是正。
    /// 旧実装は Regions を落としており、保護領域つきバリアントを Fork すると静かに消えていた)。
    /// </summary>
    internal static ImageCopy CloneWithNewId(ImageCopy source, Guid newId, string newName, DateTimeOffset now) => new()
    {
        Id = newId,
        AssetId = source.AssetId,
        CopyName = newName,
        Transform = source.Transform,
        ScalingMode = source.ScalingMode,
        Alignment = source.Alignment,
        OccupySize = source.OccupySize,
        AutoCrop = source.AutoCrop,
        ManualCrop = source.ManualCrop,
        CreatedAt = now,
        UpdatedAt = now,
        Regions = CloneRegions(source.Regions, newId),
    };

    /// <summary>
    /// 保護領域を新バリアントへ複製する。各 Region は新しい <see cref="ProtectedRegion.Id"/> と
    /// 新バリアントの <see cref="ProtectedRegion.ImageCopyId"/> を持つ独立インスタンスにし、source と
    /// Id / FK / インスタンスを共有しない (片方の編集がもう片方へ漏れない)。Rect / FillMode /
    /// オフセット / 回転 / 反転 / SortOrder などの内容はそのまま引き継ぐ。
    /// </summary>
    internal static ImmutableArray<ProtectedRegion> CloneRegions(
        ImmutableArray<ProtectedRegion> source,
        Guid newCopyId)
    {
        if (source.IsDefaultOrEmpty)
            return ImmutableArray<ProtectedRegion>.Empty;

        var builder = ImmutableArray.CreateBuilder<ProtectedRegion>(source.Length);
        foreach (var r in source)
        {
            builder.Add(new ProtectedRegion
            {
                Id = Guid.NewGuid(),
                ImageCopyId = newCopyId,
                Rect = r.Rect,
                FillMode = r.FillMode,
                FillColor = r.FillColor,
                OffsetXPx = r.OffsetXPx,
                OffsetYPx = r.OffsetYPx,
                Rotation = r.Rotation,
                FlipX = r.FlipX,
                FlipY = r.FlipY,
                SortOrder = r.SortOrder,
            });
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Placement の <see cref="GridPlacement.CopyId"/> を差し替えた複製を作る。
    /// CopyId は init-only なので新インスタンスを生成する必要がある。
    /// </summary>
    internal static GridPlacement WithCopyId(GridPlacement source, Guid newCopyId) => new()
    {
        Id = source.Id,
        GridId = source.GridId,
        CopyId = newCopyId,
        Position = source.Position,
        OccupySize = source.OccupySize,
        PixelOffsetX = source.PixelOffsetX,
        PixelOffsetY = source.PixelOffsetY,
        PlacementOrder = source.PlacementOrder,
        CreatedAt = source.CreatedAt,
    };

    private static string MakeForkName(string? sourceName)
    {
        // 派生バリアント名は DB に永続化されるため culture invariant な値を使う。
        // 表示文字列 (Terminology.VariantKey) ではなく VariantInvariant を選ぶ理由:
        // 後で言語切替しても既存 DB に残った fork 名が UI 上で意味不明にならないようにする。
        var basis = string.IsNullOrWhiteSpace(sourceName) ? Terminology.VariantInvariant : sourceName;
        return $"{basis} (派生)";
    }
}

/// <summary>
/// <see cref="ForkPlacementVariantUseCase.ExecuteAsync"/> の結果。Command / VM 側で
/// Undo 用 snapshot とユーザー通知用の名前を取り出すために値を返す。
/// </summary>
public sealed record ForkPlacementVariantResult(
    Guid NewCopyId,
    Guid OriginalCopyId,
    ImageCopy NewCopySnapshot);
