using System;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブの「論理コピー候補」リスト 1 行分。
/// 配置元として選択するために、所属アセットとサムネ情報を保持する。
/// </summary>
public sealed class CopyCandidateViewModel
{
    public Guid CopyId { get; }
    public Guid AssetId { get; }
    public string AssetFilename { get; }
    public string CopyDisplayName { get; }
    public string? ThumbnailPath { get; }
    public OccupySize OccupySize { get; }
    public Rotation Rotation { get; }

    public string SummaryLine =>
        $"{OccupySize.Width}×{OccupySize.Height} / {(int)Rotation}°";

    public CopyCandidateViewModel(ImageCopy copy, ImageAsset asset, string? thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(asset);
        CopyId = copy.Id;
        AssetId = asset.Id;
        AssetFilename = asset.OriginalFilename ?? asset.FileHash[..8];
        CopyDisplayName = string.IsNullOrWhiteSpace(copy.CopyName) ? "既定" : copy.CopyName!;
        ThumbnailPath = thumbnailPath;
        OccupySize = copy.OccupySize;
        Rotation = copy.Transform.Rotation;
    }
}
