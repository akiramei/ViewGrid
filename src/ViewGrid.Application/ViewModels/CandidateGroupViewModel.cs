using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Application.Localization;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブの「配置元バリアント」リストを Asset 単位でまとめたグループ行。
/// TreeView の親ノードとして表示され、子ノードに <see cref="CopyCandidateViewModel"/> が並ぶ。
/// バリアント数が増えたとき視覚的に整理する目的で導入（フラット表示では同じ画像のバリアントが
/// アセット名のテキストで判別するしかなく、画像数が増えると視認性が落ちるため）。
/// </summary>
public sealed partial class CandidateGroupViewModel : ObservableObject
{
    public Guid AssetId { get; }
    public string AssetFilename { get; }

    /// <summary>
    /// このアセットに紐づくバリアント。<see cref="GridWorkspaceViewModel"/> から
    /// 追加/削除されると即座に View に反映される。
    /// </summary>
    public ObservableCollection<CopyCandidateViewModel> Variants { get; } = [];

    public CandidateGroupViewModel(Guid assetId, string assetFilename)
    {
        AssetId = assetId;
        AssetFilename = assetFilename;
        Variants.CollectionChanged += OnVariantsChanged;
    }

    /// <summary>
    /// グループヘッダに表示するサマリ（例: "3 バリアント"）。
    /// </summary>
    public string SummaryLine => $"{Variants.Count} {Terminology.Variant}";

    /// <summary>
    /// TreeView での展開状態。<c>TreeViewItem.IsExpanded</c> に TwoWay バインドされ、
    /// ユーザーがクリックで展開/折り畳みするとここに保存される。既定 <c>true</c>（展開）。
    /// 候補ライブラリ再ロード時はインスタンスが維持されるため、この値も維持される。
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    private void OnVariantsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SummaryLine));
    }
}
