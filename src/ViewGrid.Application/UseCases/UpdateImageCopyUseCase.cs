using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 論理コピー（バリアント）の特性・変形を更新する。
/// <para>
/// OccupySize は配置（GridPlacement）単位の固有特性へ移管されたため、本 UseCase からは
/// 検証ロジックを撤去した。<see cref="UpdateImageCopyChanges.OccupySize"/> は新規配置時の
/// デフォルト初期値の保持目的で残置されており、本 UseCase 経由で更新しても既存配置は影響を
/// 受けない（次の新規配置の OccupySize 初期値だけが変わる）。
/// </para>
/// </summary>
public sealed class UpdateImageCopyUseCase(
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository,
    IGridCanvasRepository gridRepository)
{
    // placementRepository / gridRepository は将来の検証ロジック追加に備えて DI に残している
    // が、現状は未使用（OccupySize 検証撤去後）。CS9113 警告を抑止するためフィールドへ
    // 待避する（_ プレフィックスで「未使用フィールド」を明示）。
    private readonly IGridPlacementRepository _placementRepository = placementRepository;
    private readonly IGridCanvasRepository _gridRepository = gridRepository;

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
            OccupySize = changes.OccupySize ?? current.OccupySize,
            AutoCrop = updatedAutoCrop,
            ManualCrop = updatedManualCrop,
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

    /// <summary>
    /// バリアントのデフォルト OccupySize（新規配置時の初期値として継承される）。
    /// 既存配置の占有セルには影響しない（配置単位で独立しているため）。
    /// 通常の編集 UI からは触らない（CopyPropertiesView から撤去済み）。
    /// </summary>
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
