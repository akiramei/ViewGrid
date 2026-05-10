using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 「保護領域」 タブの ListBox 1 行分。 <see cref="ProtectedRegion"/> をラップして
/// UI 編集用に <see cref="Rect"/> 等を可変にする。 <see cref="Id"/> は Undo / Editor 連携の
/// 参照キーとして安定 (UI 編集中も新規 Region が作られた瞬間に決まる)。
/// </summary>
public sealed partial class ProtectedRegionItemViewModel : ObservableObject
{
    public Guid Id { get; }

    /// <summary>元画像座標系 0–1 の bbox。 Editor 終了時に <see cref="CopyPropertiesViewModel.UpdateRegionRect"/>
    /// 経由で更新される。 setter 経由で IsDirty が立つ。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelText))]
    public partial RegionRectFraction Rect { get; set; }

    /// <summary>親側塗りつぶしの方法。 White / Black / Transparent / Custom。</summary>
    [ObservableProperty]
    public partial ProtectedRegionFillMode FillMode { get; set; }

    /// <summary><see cref="FillMode"/>=Custom のときに使う ARGB 色。 それ以外では無視。</summary>
    [ObservableProperty]
    public partial uint? FillColor { get; set; }

    /// <summary>セル内 pixel 単位の X オフセット (左上基準、 既定 0、 負値可)。</summary>
    [ObservableProperty]
    public partial int OffsetXPx { get; set; }

    /// <summary>セル内 pixel 単位の Y オフセット (左上基準、 既定 0、 負値可)。</summary>
    [ObservableProperty]
    public partial int OffsetYPx { get; set; }

    /// <summary>表示用ラベル。 fraction を %、 W/H を 2 桁の小数点以下まで表示する。</summary>
    public string LabelText =>
        $"X {Rect.X * 100:F1}% / Y {Rect.Y * 100:F1}% / W {Rect.Width * 100:F1}% / H {Rect.Height * 100:F1}%";

    public ProtectedRegionItemViewModel(
        Guid id,
        RegionRectFraction rect,
        ProtectedRegionFillMode fillMode,
        uint? fillColor = null,
        int offsetXPx = 0,
        int offsetYPx = 0)
    {
        Id = id;
        Rect = rect;
        FillMode = fillMode;
        FillColor = fillColor;
        OffsetXPx = offsetXPx;
        OffsetYPx = offsetYPx;
    }

    /// <summary>既存 <see cref="ProtectedRegion"/> から VM を作る。 Id を引き継ぐので Undo の参照キーとして安定。</summary>
    public static ProtectedRegionItemViewModel From(ProtectedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new ProtectedRegionItemViewModel(
            region.Id,
            region.Rect,
            region.FillMode,
            region.FillColor,
            region.OffsetXPx,
            region.OffsetYPx);
    }

    /// <summary>新規 region 用のデフォルト矩形 (画像中央 20%)。 Editor 起動前のプレースホルダ値。</summary>
    public static RegionRectFraction DefaultRect { get; } = new(0.4, 0.4, 0.2, 0.2);
}
