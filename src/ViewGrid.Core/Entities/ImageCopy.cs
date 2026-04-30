using System;

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

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>スケーリング・アライメントの集約ビュー。</summary>
    public ImageCharacteristics Characteristics => new(ScalingMode, Alignment);
}
