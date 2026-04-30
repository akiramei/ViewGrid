using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 論理コピー一覧の各アイテム。編集結果の View 反映を兼ねて可変プロパティを持つ。
/// </summary>
public sealed partial class CopyItemViewModel : ObservableObject
{
    public Guid CopyId { get; }
    public Guid AssetId { get; }

    /// <summary>
    /// アセットのサムネ絶対パス（無ければ <c>null</c>）。AutoCrop の画像クリックピッカー
    /// が「サムネ上をクリックして色を採取」する UI で使う。
    /// </summary>
    public string? ThumbnailPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial string? CopyName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial Rotation Rotation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial bool FlipX { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial bool FlipY { get; set; }

    [ObservableProperty]
    public partial ScalingMode ScalingMode { get; set; }

    [ObservableProperty]
    public partial Alignment Alignment { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial OccupySize OccupySize { get; set; }

    /// <summary>単色余白の自動トリミング設定。<c>null</c> なら機能 OFF。</summary>
    [ObservableProperty]
    public partial AutoCropSettings? AutoCrop { get; set; }

    public CopyItemViewModel(ImageCopy copy, string? thumbnailPath = null)
    {
        ArgumentNullException.ThrowIfNull(copy);
        CopyId = copy.Id;
        AssetId = copy.AssetId;
        CopyName = copy.CopyName;
        Rotation = copy.Transform.Rotation;
        FlipX = copy.Transform.FlipX;
        FlipY = copy.Transform.FlipY;
        ScalingMode = copy.ScalingMode;
        Alignment = copy.Alignment;
        OccupySize = copy.OccupySize;
        AutoCrop = copy.AutoCrop;
        ThumbnailPath = thumbnailPath;
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CopyName) ? "既定" : CopyName!;

    public string SummaryLine =>
        $"{OccupySize.Width}×{OccupySize.Height} / {(int)Rotation}°{(FlipX ? " H" : "")}{(FlipY ? " V" : "")}";
}
