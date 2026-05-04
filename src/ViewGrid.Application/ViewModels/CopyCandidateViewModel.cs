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
    public Rotation Rotation { get; }

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
        ? Terminology.VariantUnnamed
        : CopyName!;

    public string SummaryLine =>
        $"{OccupySize.Width}×{OccupySize.Height} / {(int)Rotation}°";

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
        Rotation = copy.Transform.Rotation;
    }
}
