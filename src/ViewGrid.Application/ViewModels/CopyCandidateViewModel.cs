using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブの「配置元バリアント」リスト 1 行分。配置元として選択するために
/// 所属アセットとサムネ情報を保持する。配置ファースト UI 第 2 段階で
/// インラインリネーム編集状態（<see cref="IsEditing"/> / <see cref="EditingName"/>）も
/// 持つようになり、<c>CopyName</c> はリネーム永続化後の即時表示更新のため可変化した。
/// </summary>
public sealed partial class CopyCandidateViewModel : ObservableObject
{
    public Guid CopyId { get; }
    public Guid AssetId { get; }
    public string AssetFilename { get; }
    public string? ThumbnailPath { get; }
    public OccupySize OccupySize { get; }

    /// <summary>元画像の幅 (px)。 手動 crop の寸法を px で示す要約に使う。</summary>
    public int SourceWidth { get; }

    /// <summary>元画像の高さ (px)。</summary>
    public int SourceHeight { get; }

    /// <summary>
    /// 回転・crop・保護領域は、 候補リストの要約に出すため可変にして、 保存・Undo・再読込のたびに
    /// <see cref="ApplyCopy"/> で DB の最新値へ同期する (古い要約が残ると候補の見分けが付かなくなる)。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    public partial Rotation Rotation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeLine))]
    [NotifyPropertyChangedFor(nameof(HasBadges))]
    public partial ManualCropFraction? ManualCrop { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeLine))]
    [NotifyPropertyChangedFor(nameof(HasBadges))]
    public partial AutoCropSettings? AutoCrop { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeLine))]
    [NotifyPropertyChangedFor(nameof(HasBadges))]
    public partial int RegionCount { get; set; }

    /// <summary>
    /// バリアント名。<c>null</c> または空白だけなら <see cref="CopyDisplayName"/> は
    /// <see cref="Terminology.VariantUnnamed"/> を返す。リネーム永続化後は VM 側で
    /// 直接代入され、View にも即座に反映される。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyDisplayName))]
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
    /// <see cref="GridWorkspaceViewModel.CommitEditCandidateAsync"/> 経由で永続化される。
    /// </summary>
    [ObservableProperty]
    public partial string? EditingName { get; set; }

    /// <summary>
    /// TreeView の <c>TreeViewItem.IsExpanded</c> 共通バインド先。葉ノードなので意味を持たないが、
    /// グループ用 (<see cref="CandidateGroupViewModel.IsExpanded"/>) と同名プロパティを揃えて
    /// バインドエラーを抑止する。<c>false</c> 固定（葉なので展開操作は無視される）。
    /// </summary>
    public bool IsExpanded { get; set; }

    public string CopyDisplayName => string.IsNullOrWhiteSpace(CopyName)
        ? LocAccessor.Current[Terminology.VariantUnnamedKey]
        : CopyName!;

    public string SummaryLine =>
        $"{OccupySize.Width}×{OccupySize.Height} / {(int)Rotation}°";

    /// <summary>
    /// 同じ元画像から作った候補をサムネ (元アセットと共通) だけでは見分けられないため、 加工内容のバッジを
    /// 文字で示す: 手動 crop (px 寸法) / 自動 crop / 保護領域の数。 加工が無ければ空。
    /// </summary>
    public string BadgeLine
    {
        get
        {
            var loc = LocAccessor.Current;
            var parts = new List<string>();
            if (ManualCrop is { } m)
            {
                var (_, _, w, h) = m.ToPixelBbox(SourceWidth, SourceHeight);
                parts.Add(SourceWidth > 0 && SourceHeight > 0 && w > 0 && h > 0
                    ? loc.Format("Candidate_CropManualFmt", w, h)
                    : loc["Candidate_CropManual"]);
            }
            else if (AutoCrop is not null)
            {
                parts.Add(loc["Candidate_CropAuto"]);
            }
            if (RegionCount > 0)
                parts.Add(loc.Format("Candidate_RegionsFmt", RegionCount));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>加工バッジを出すか (View の IsVisible 用)。</summary>
    public bool HasBadges => !string.IsNullOrEmpty(BadgeLine);

    /// <summary>
    /// DB の最新の <see cref="ImageCopy"/> から、 要約に出す加工内容 (回転・crop・保護領域) を同期する。
    /// 名前の同期は呼び出し側 (<c>LoadCandidatesAsync</c>) が行う。
    /// </summary>
    public void ApplyCopy(ImageCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        Rotation = copy.Transform.Rotation;
        ManualCrop = copy.ManualCrop;
        AutoCrop = copy.AutoCrop;
        RegionCount = copy.Regions.IsDefault ? 0 : copy.Regions.Length;
    }

    public CopyCandidateViewModel(ImageCopy copy, ImageAsset asset, string? thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(asset);
        CopyId = copy.Id;
        AssetId = asset.Id;
        AssetFilename = asset.OriginalFilename ?? asset.FileHash[..8];
        CopyName = copy.CopyName;
        ThumbnailPath = thumbnailPath;
        OccupySize = copy.OccupySize;
        SourceWidth = asset.Size.Width;
        SourceHeight = asset.Size.Height;
        ApplyCopy(copy);
    }
}
