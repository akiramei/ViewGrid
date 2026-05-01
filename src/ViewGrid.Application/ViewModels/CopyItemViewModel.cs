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
    /// では「サムネ画像を表示してクリック」する UI として使うが、色の採取は
    /// <see cref="SourceImagePath"/> から行う（サムネは WebP 圧縮で色が変化するため）。
    /// </summary>
    public string? ThumbnailPath { get; }

    /// <summary>
    /// 原画像（圧縮なし）の絶対パス。AutoCrop の色採取は本パスから取得することで、
    /// サムネ圧縮による色のズレを避け、AutoCrop 走査と同一の色で一致する。
    /// </summary>
    public string? SourceImagePath { get; }

    /// <summary>原画像のピクセル幅（サムネクリック座標 → 原画像座標換算に使う）。</summary>
    public int SourceWidth { get; }

    /// <summary>原画像のピクセル高さ（同上）。</summary>
    public int SourceHeight { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial string? CopyName { get; set; }

    /// <summary>
    /// リスト上でインラインリネーム編集中かどうか。<c>true</c> の間だけ View 側で
    /// TextBlock が TextBox に切り替わる。F2 / ダブルクリックで <c>true</c>、Enter / フォーカス喪失で
    /// 確定 → <c>false</c>、Esc でキャンセル → <c>false</c>。
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>
    /// インラインリネーム中の編集バッファ。<see cref="IsEditing"/>=true で View にバインドされる。
    /// 編集開始時に <see cref="CopyName"/> を初期値としてコピー、確定時に
    /// <see cref="CopyListViewModel.RenameCopyAsync"/> 経由で永続化される。
    /// </summary>
    [ObservableProperty]
    public partial string? EditingName { get; set; }

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

    /// <summary>任意矩形トリミング設定（0–1 比率）。<c>null</c> なら機能 OFF。
    /// AutoCrop と同時 ON でも ManualCrop が排他的に勝つ（Resolver で判定）。</summary>
    [ObservableProperty]
    public partial ManualCropFraction? ManualCrop { get; set; }

    public CopyItemViewModel(
        ImageCopy copy,
        string? thumbnailPath = null,
        string? sourceImagePath = null,
        int sourceWidth = 0,
        int sourceHeight = 0)
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
        ManualCrop = copy.ManualCrop;
        ThumbnailPath = thumbnailPath;
        SourceImagePath = sourceImagePath;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CopyName) ? "既定" : CopyName!;

    public string SummaryLine =>
        $"{OccupySize.Width}×{OccupySize.Height} / {(int)Rotation}°{(FlipX ? " H" : "")}{(FlipY ? " V" : "")}";
}
