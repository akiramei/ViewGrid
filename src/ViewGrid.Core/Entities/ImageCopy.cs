using System.Collections.Immutable;

namespace ViewGrid.Core.Entities;

/// <summary>
/// 論理コピー。1 つの <see cref="ImageAsset"/> から複数作成でき、それぞれが独立した
/// 変形・画像特性・占有セル設定を持つ。
/// </summary>
public sealed class ImageCopy
{
    public required Guid Id { get; init; }
    public required Guid AssetId { get; init; }

    /// <summary>ユーザー定義のコピー名（省略時は自動生成）。</summary>
    public string? CopyName { get; init; }

    public required ImageTransform Transform { get; init; }

    public required ScalingMode ScalingMode { get; init; }

    /// <summary>
    /// セル内での画像の位置基準点。画像 ≤ セル の軸では「セル内のどこに配置するか」、
    /// 画像 &gt; セル の軸では「ソースのどの部分を見せるか」を**同じ値**で表現する
    /// （CSS background-position 等と同じ単一アンカー設計）。
    /// </summary>
    public required Alignment Alignment { get; init; }

    public required OccupySize OccupySize { get; init; }

    /// <summary>
    /// 単色余白の自動トリミングの対象色（ARGB 32-bit、α が 0 のとき α 単独判定）。
    /// <c>null</c> + <see cref="AutoCropThreshold"/>=null なら機能 OFF（既定）。
    /// EF Core が直接マップする原始値。アプリケーション側は <see cref="AutoCrop"/> で集約してアクセスする。
    /// </summary>
    public uint? AutoCropTargetColor { get; init; }

    /// <summary>単色余白の自動トリミング閾値（0–255、Chebyshev 距離）。</summary>
    public byte? AutoCropThreshold { get; init; }

    /// <summary>
    /// 単色余白の自動トリミング設定の集約ビュー。<c>null</c> なら機能 OFF。
    /// <see cref="AutoCropTargetColor"/> / <see cref="AutoCropThreshold"/> の両方が
    /// 揃っているときだけ非 null で返す（DB の整合は EF Core 側のマップで保証）。
    /// </summary>
    public AutoCropSettings? AutoCrop
    {
        get => (AutoCropTargetColor, AutoCropThreshold) switch
        {
            ({ } c, { } t) => new AutoCropSettings(c, t),
            _ => null,
        };
        init
        {
            AutoCropTargetColor = value?.TargetColorArgb;
            AutoCropThreshold = value?.Threshold;
        }
    }

    /// <summary>
    /// 任意矩形トリミングの bbox（0–1 比率、元画像座標系）。<c>null</c> なら機能 OFF。
    /// EF Core が直接マップする原始値。アプリケーション側は <see cref="ManualCrop"/> で集約してアクセスする。
    /// 4 値すべてが揃っていないと無効として扱う（DB の整合は EF Core 側のマップで保証）。
    /// </summary>
    public double? ManualCropX { get; init; }

    /// <summary>任意矩形トリミングの bbox 左上 Y（0–1 比率）。</summary>
    public double? ManualCropY { get; init; }

    /// <summary>任意矩形トリミングの bbox 幅（0–1 比率）。</summary>
    public double? ManualCropWidth { get; init; }

    /// <summary>任意矩形トリミングの bbox 高さ（0–1 比率）。</summary>
    public double? ManualCropHeight { get; init; }

    /// <summary>
    /// 任意矩形トリミング設定の集約ビュー。<c>null</c> なら機能 OFF。
    /// 4 つの原始値すべてが揃っているときだけ非 null で返す。
    /// <see cref="AutoCrop"/> と同時設定されている場合は ManualCrop が優先される（Resolver 層で判定）。
    /// </summary>
    public ManualCropFraction? ManualCrop
    {
        get => (ManualCropX, ManualCropY, ManualCropWidth, ManualCropHeight) switch
        {
            ({ } x, { } y, { } w, { } h) => new ManualCropFraction(x, y, w, h),
            _ => null,
        };
        init
        {
            ManualCropX = value?.X;
            ManualCropY = value?.Y;
            ManualCropWidth = value?.Width;
            ManualCropHeight = value?.Height;
        }
    }

    /// <summary>
    /// 親画像の回転を受けず最終キャンバス水平で再描画される 「保護領域」 の配列
    /// (PhotoBoard 出力時のみ有効、 描画順は <see cref="ProtectedRegion.SortOrder"/>)。
    /// EF Core では子テーブル <c>image_copy_regions</c> を独立 entity としてマップしており、
    /// 本プロパティは Repository 側で手動アセンブルする (<c>ImageCopyConfiguration</c> 側で Ignore)。
    /// 既定は空配列。
    /// </summary>
    /// <remarks>
    /// <see cref="UpdatedAt"/> と同様に <c>set</c> アクセサを公開する。 これは Repository が
    /// AsNoTracking で取得した <see cref="ImageCopy"/> に子テーブル由来の Regions を
    /// 「後付けで attach」 するためのもので、 通常の Use case / VM 経路で書き換える用途では
    /// ない (ImmutableArray 自体は不変なので、 アグリゲートの内部状態は実質的に保たれる)。
    /// </remarks>
    public ImmutableArray<ProtectedRegion> Regions { get; set; } = ImmutableArray<ProtectedRegion>.Empty;

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>スケーリング・アライメントの集約ビュー。</summary>
    public ImageCharacteristics Characteristics => new(ScalingMode, Alignment);
}
