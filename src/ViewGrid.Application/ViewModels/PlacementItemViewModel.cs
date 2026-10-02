using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Application.Localization;
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

    /// <summary>
    /// この配置が参照する論理コピーの Id。 「別バリアントに分岐 (Fork)」 は DB 上の placement.CopyId を
    /// 付け替えるため、 PlacementId で再利用される既存 VM も再ロード時に追従させる必要がある
    /// (<see cref="ApplyIdentity"/>)。 追従しないと編集・ライブプレビューが旧バリアントを向き続ける。
    /// </summary>
    [ObservableProperty]
    public partial Guid CopyId { get; set; }

    /// <summary>
    /// この配置が参照する論理コピーの基となるアセット ID。Inspector embed の
    /// CopyPropertiesView 用 CopyItemViewModel 構築時に asset 情報の取得に使う。
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
    /// </summary>
    [ObservableProperty]
    public partial AutoCropSettings? AutoCrop { get; set; }

    /// <summary>
    /// 任意矩形トリミング設定（コピー側の特性）。<c>null</c> なら機能 OFF。
    /// AutoCrop と同時 ON でも ManualCrop が排他的に勝つ（Resolver で判定）。
    /// </summary>
    [ObservableProperty]
    public partial ManualCropFraction? ManualCrop { get; set; }

    /// <summary>
    /// 実効的なクロップ比率（0–1）。VM 層で <see cref="ViewGrid.Core.Services.IImageCropResolver"/>
    /// 経由で解決された結果（ManualCrop 優先、それ以外で AutoCrop の走査結果）。
    /// Renderer / View / Use case が同一比率を共有することで、自動と手動の表示が揃う。
    /// クロップ無効または解決失敗時は <c>null</c>。
    /// </summary>
    [ObservableProperty]
    public partial CropFraction? EffectiveCropFraction { get; set; }

    [ObservableProperty]
    public partial int PixelOffsetX { get; set; }

    [ObservableProperty]
    public partial int PixelOffsetY { get; set; }

    public string? ThumbnailPath { get; }

    /// <summary>「アセット名 / バリアント名」 表示。 バリアントの改名・分岐で変わるため <see cref="ApplyIdentity"/> で更新される。</summary>
    [ObservableProperty]
    public partial string Label { get; set; }

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
        // OccupySize は配置単位の固有特性として placement から直接取得する。
        OccupySize = placement.OccupySize;
        Rotation = copy.Transform.Rotation;
        FlipX = copy.Transform.FlipX;
        FlipY = copy.Transform.FlipY;
        ScalingMode = copy.ScalingMode;
        Alignment = copy.Alignment;
        AutoCrop = copy.AutoCrop;
        ManualCrop = copy.ManualCrop;
        ThumbnailPath = thumbnailPath;
        SourceWidth = asset.Size.Width;
        SourceHeight = asset.Size.Height;
        Label = BuildLabel(copy, asset);
    }

    private static string BuildLabel(ImageCopy copy, ImageAsset asset)
    {
        var assetLabel = asset.OriginalFilename ?? asset.FileHash[..8];
        var copyLabel = string.IsNullOrWhiteSpace(copy.CopyName) ? LocAccessor.Current[Terminology.VariantUnnamedKey] : copy.CopyName!;
        return $"{assetLabel} / {copyLabel}";
    }

    /// <summary>
    /// 再ロードで DB 上の参照先バリアント (<see cref="GridPlacement.CopyId"/>) とラベルへ追従する。
    /// <see cref="PlacementId"/> が同じでも Fork で CopyId が変わりうるため、 差分更新で再利用する既存 VM に
    /// 必ず適用する。 呼び出し側は直後に <see cref="ApplyCopyChanges(ImageCopy, CropFraction?)"/> で共有特性も揃える。
    /// </summary>
    public void ApplyIdentity(GridPlacement placement, ImageCopy copy, ImageAsset asset)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(asset);
        CopyId = placement.CopyId;
        Label = BuildLabel(copy, asset);
    }

    /// <summary>編集後の <see cref="ImageCopy"/> から共有特性を反映する。
    /// OccupySize は配置単位なのでここでは更新しない（配置別の更新ルートで個別に反映する）。
    /// <para>
    /// <see cref="EffectiveCropFraction"/> は AutoCrop の場合ピクセル走査を伴う派生値なので、
    /// ここでは更新しない。 呼び出し側 (<see cref="ViewGrid.Core.Services.IImageCropResolver"/> 経由で
    /// LoadPlacements / Rollback) が ApplyCopyChanges の直後に必ず再解決して代入する責務を持つ。
    /// </para></summary>
    public void ApplyCopyChanges(ImageCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        Rotation = copy.Transform.Rotation;
        FlipX = copy.Transform.FlipX;
        FlipY = copy.Transform.FlipY;
        ScalingMode = copy.ScalingMode;
        Alignment = copy.Alignment;
        AutoCrop = copy.AutoCrop;
        ManualCrop = copy.ManualCrop;
    }

    /// <summary>
    /// <see cref="ApplyCopyChanges(ImageCopy)"/> に加えて、 resolver で再解決済みの実効クロップ比率も
    /// 同一呼び出しで反映する。 raw な crop 設定と派生値 (<see cref="EffectiveCropFraction"/>) を 1 メソッドに
    /// まとめることで、 「ApplyCopyChanges 後に EffectiveCropFraction の代入を忘れて不整合になる」 経路を
    /// 構造的に塞ぐ。 <see cref="EffectiveCropFraction"/> を最後に代入するため、 canvas (描画は
    /// EffectiveCropFraction を真理値とし、 GridCanvasView 側で 1 ディスパッチに rebuild を debounce する) には
    /// 確定後の値だけが反映され、 中間状態は描画されない。
    /// </summary>
    public void ApplyCopyChanges(ImageCopy copy, CropFraction? effectiveCrop)
    {
        ApplyCopyChanges(copy);
        EffectiveCropFraction = effectiveCrop;
    }
}
