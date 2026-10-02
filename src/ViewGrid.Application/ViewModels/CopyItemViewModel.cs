using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Application.Localization;
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

    // インラインリネーム編集状態（IsEditing / EditingName）は CopyCandidateViewModel
    // (配置タブ候補ツリー) 側で別途保持する。本 VM はバリアントの「特性編集」コンテキスト
    // 専用なのでリネーム編集状態は不要。

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

    // OccupySize は配置 (GridPlacement) 単位の固有特性へ移管されたため、バリアント候補
    // リストの表示プロパティとしては保持しない（表示すると「バリアントの占有」と
    // 「配置済みの占有」が異なるケースが混乱を招くため）。

    /// <summary>単色余白の自動トリミング設定。<c>null</c> なら機能 OFF。</summary>
    [ObservableProperty]
    public partial AutoCropSettings? AutoCrop { get; set; }

    /// <summary>任意矩形トリミング設定（0–1 比率）。<c>null</c> なら機能 OFF。
    /// AutoCrop と同時 ON でも ManualCrop が排他的に勝つ（Resolver で判定）。</summary>
    [ObservableProperty]
    public partial ManualCropFraction? ManualCrop { get; set; }

    /// <summary>
    /// PhotoBoard 出力時の保護領域 (<see cref="ProtectedRegion"/>) 集約。 Phase 1 では
    /// PhotoBoard 経路でのみ親側白塗り + canvas 水平 overlay として使われる。 通常モード
    /// (Normal) では完全無視。 既定は空配列。
    /// </summary>
    [ObservableProperty]
    public partial ImmutableArray<ProtectedRegion> Regions { get; set; } = ImmutableArray<ProtectedRegion>.Empty;

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
        AutoCrop = copy.AutoCrop;
        ManualCrop = copy.ManualCrop;
        Regions = copy.Regions;
        ThumbnailPath = thumbnailPath;
        SourceImagePath = sourceImagePath;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    /// <summary>
    /// 別スナップショット (<paramref name="other"/>) と編集対象の値が完全に一致するか。 2 つの編集パネル
    /// (Inspector 内 / 候補単体) が同じ CopyId の別スナップショットを持つため、 片方の保存後に
    /// もう片方が古いかどうかを判定して再同期の要否を決めるのに使う。 表示専用の
    /// (サムネ・原画像パス) は比較しない。
    /// </summary>
    public bool HasSameContentAs(CopyItemViewModel other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CopyId == other.CopyId
            && string.Equals(CopyName, other.CopyName, StringComparison.Ordinal)
            && Rotation == other.Rotation
            && FlipX == other.FlipX
            && FlipY == other.FlipY
            && ScalingMode == other.ScalingMode
            && Alignment == other.Alignment
            && AutoCrop == other.AutoCrop
            && ManualCrop == other.ManualCrop
            && SourceWidth == other.SourceWidth
            && SourceHeight == other.SourceHeight
            && RegionsEqual(Regions, other.Regions);
    }

    private static bool RegionsEqual(ImmutableArray<ProtectedRegion> a, ImmutableArray<ProtectedRegion> b)
    {
        var left = a.IsDefault ? ImmutableArray<ProtectedRegion>.Empty : a;
        var right = b.IsDefault ? ImmutableArray<ProtectedRegion>.Empty : b;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            var x = left[i];
            var y = right[i];
            if (x.Id != y.Id || x.Rect != y.Rect || x.FillMode != y.FillMode || x.FillColor != y.FillColor
                || x.OffsetXPx != y.OffsetXPx || x.OffsetYPx != y.OffsetYPx || x.Rotation != y.Rotation
                || x.FlipX != y.FlipX || x.FlipY != y.FlipY || x.SortOrder != y.SortOrder)
                return false;
        }
        return true;
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CopyName) ? LocAccessor.Current[Terminology.VariantUnnamedKey] : CopyName!;

    public string SummaryLine =>
        $"{(int)Rotation}°{(FlipX ? " H" : "")}{(FlipY ? " V" : "")}";
}
