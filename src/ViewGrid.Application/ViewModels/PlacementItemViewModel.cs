using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置済みアイテムの View 表示用。CellPosition と OccupySize を持ち、
/// セル分割キャンバス上に Border 等で重ねて表示する想定。
/// 配置タブのインスペクタで編集された結果が即座に View に反映されるよう、
/// 共有特性（Rotation/Flip/Scaling/Trim/Align/Occupy）と PixelOffsetX/Y を ObservableProperty 化する。
/// </summary>
public sealed partial class PlacementItemViewModel : ObservableObject
{
    public Guid PlacementId { get; }
    public Guid GridId { get; }
    public Guid CopyId { get; }

    /// <summary>
    /// この配置が参照する論理コピーの基となるアセット ID。Inspector から
    /// 「特性を編集 →」で <see cref="ViewGrid.Application.Messages.NavigateToCopyPropertiesMessage"/>
    /// を送る際に必要となる（受信側で準備タブのアセット選択を伴うため）。
    /// </summary>
    public Guid AssetId { get; }

    [ObservableProperty]
    public partial CellPosition Position { get; set; }

    [ObservableProperty]
    public partial OccupySize OccupySize { get; set; }

    [ObservableProperty]
    public partial Rotation Rotation { get; set; }

    [ObservableProperty]
    public partial bool FlipX { get; set; }

    [ObservableProperty]
    public partial bool FlipY { get; set; }

    [ObservableProperty]
    public partial ScalingMode ScalingMode { get; set; }

    [ObservableProperty]
    public partial Alignment Alignment { get; set; }

    /// <summary>
    /// 単色余白の自動トリミング設定（コピー側の特性）。<c>null</c> なら機能 OFF。
    /// View 側でサムネに反映され、Renderer 側で PNG 出力にも反映される。
    /// </summary>
    [ObservableProperty]
    public partial AutoCropSettings? AutoCrop { get; set; }

    /// <summary>
    /// AutoCrop 走査結果の比率（0–1）。VM 層で <see cref="IAutoCropBboxResolver"/> 経由で
    /// 原画像走査した結果を保存。Renderer / View / Use case が同一比率を共有することで、
    /// サムネ走査と原画像走査の精度差による表示不整合を避ける。
    /// AutoCrop OFF または走査失敗時は <c>null</c>。
    /// </summary>
    [ObservableProperty]
    public partial AutoCropFraction? AutoCropFraction { get; set; }

    [ObservableProperty]
    public partial int PixelOffsetX { get; set; }

    [ObservableProperty]
    public partial int PixelOffsetY { get; set; }

    public string? ThumbnailPath { get; }
    public string Label { get; }

    /// <summary>元画像（回転前）のピクセル幅。Stretch.None 表示と Renderer の整合に使う。</summary>
    public int SourceWidth { get; }

    /// <summary>元画像（回転前）のピクセル高さ。</summary>
    public int SourceHeight { get; }

    public int GridX => Position.X;
    public int GridY => Position.Y;
    public int OccupyWidth => OccupySize.Width;
    public int OccupyHeight => OccupySize.Height;

    public PlacementItemViewModel(GridPlacement placement, ImageCopy copy, ImageAsset asset, string? thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(asset);
        PlacementId = placement.Id;
        GridId = placement.GridId;
        CopyId = placement.CopyId;
        AssetId = copy.AssetId;
        Position = placement.Position;
        PixelOffsetX = placement.PixelOffsetX;
        PixelOffsetY = placement.PixelOffsetY;
        OccupySize = copy.OccupySize;
        Rotation = copy.Transform.Rotation;
        FlipX = copy.Transform.FlipX;
        FlipY = copy.Transform.FlipY;
        ScalingMode = copy.ScalingMode;
        Alignment = copy.Alignment;
        AutoCrop = copy.AutoCrop;
        ThumbnailPath = thumbnailPath;
        SourceWidth = asset.Size.Width;
        SourceHeight = asset.Size.Height;
        var assetLabel = asset.OriginalFilename ?? asset.FileHash[..8];
        var copyLabel = string.IsNullOrWhiteSpace(copy.CopyName) ? "既定" : copy.CopyName!;
        Label = $"{assetLabel} / {copyLabel}";
    }

    /// <summary>編集後の <see cref="ImageCopy"/> から共有特性を反映する。</summary>
    public void ApplyCopyChanges(ImageCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        OccupySize = copy.OccupySize;
        Rotation = copy.Transform.Rotation;
        FlipX = copy.Transform.FlipX;
        FlipY = copy.Transform.FlipY;
        ScalingMode = copy.ScalingMode;
        Alignment = copy.Alignment;
        AutoCrop = copy.AutoCrop;
    }
}
