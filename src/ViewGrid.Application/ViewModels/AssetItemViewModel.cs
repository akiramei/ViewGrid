using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// アセット一覧の単一アイテム表示用 ViewModel。不変。
/// </summary>
public sealed class AssetItemViewModel
{
    public AssetItemViewModel(ImageAsset asset, string? thumbnailAbsolutePath)
    {
        ArgumentNullException.ThrowIfNull(asset);

        AssetId = asset.Id;
        DisplayName = asset.OriginalFilename ?? asset.FileHash[..8];
        Width = asset.Size.Width;
        Height = asset.Size.Height;
        FileSizeBytes = asset.FileSizeBytes;
        ThumbnailAbsolutePath = thumbnailAbsolutePath;
    }

    public Guid AssetId { get; }
    public string DisplayName { get; }
    public int Width { get; }
    public int Height { get; }
    public long FileSizeBytes { get; }

    /// <summary>サムネイルの絶対パス（未生成なら null）。View 側で Bitmap 化する。</summary>
    public string? ThumbnailAbsolutePath { get; }

    public string Dimensions => $"{Width}×{Height}";

    public string FileSizeDisplay => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB",
    };
}
