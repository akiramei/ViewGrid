using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 既存のバリアント (<see cref="ImageCopy"/>) を、 設定ごと複製して新しいバリアントを作る。
/// 元画像から初期値で作る <see cref="CreateLogicalCopyUseCase"/> と違い、 回転・反転・スケーリング・
/// 縦横揃え・占有・AutoCrop / ManualCrop・保護領域をすべて引き継ぐ (未配置の比較案を、 仮配置や再入力なしで増やせる)。
/// 元のバリアントと、 それを参照する既存の配置は変わらない。 配置 1 件だけを分岐する
/// <see cref="ForkPlacementVariantUseCase"/> とは別で、 配置を触らない。
/// </summary>
public sealed class DuplicateImageCopyUseCase(IImageCopyRepository copyRepository)
{
    /// <param name="sourceCopyId">複製元のバリアント。</param>
    /// <param name="copyName">新しいバリアントの名前。 呼び出し側 (表示言語で組み立てる) が渡す。 空白だけなら無名。</param>
    public async Task<ErrorOr<ImageCopy>> ExecuteAsync(
        Guid sourceCopyId, string? copyName, CancellationToken ct = default)
    {
        var source = await copyRepository.FindByIdAsync(sourceCopyId, ct);
        if (source is null)
            return Error.NotFound("ImageCopy.NotFound", $"ImageCopy {sourceCopyId} が見つかりません。");

        var name = string.IsNullOrWhiteSpace(copyName) ? null : copyName.Trim();

        // Id・名前・日時だけ差し替える。 保護領域も新しい Id / FK の独立インスタンスにする (Fork と同じ複製)。
        var clone = ForkPlacementVariantUseCase.CloneWithNewId(
            source, Guid.NewGuid(), name ?? string.Empty, DateTimeOffset.UtcNow);

        // CloneWithNewId は名前を非 null で受けるため、 無名にしたい場合だけ CopyName を戻して作り直す。
        var copy = name is null
            ? new ImageCopy
            {
                Id = clone.Id,
                AssetId = clone.AssetId,
                CopyName = null,
                Transform = clone.Transform,
                ScalingMode = clone.ScalingMode,
                Alignment = clone.Alignment,
                OccupySize = clone.OccupySize,
                AutoCrop = clone.AutoCrop,
                ManualCrop = clone.ManualCrop,
                CreatedAt = clone.CreatedAt,
                UpdatedAt = clone.UpdatedAt,
                Regions = clone.Regions,
            }
            : clone;

        return await copyRepository.AddAsync(copy, ct);
    }
}
