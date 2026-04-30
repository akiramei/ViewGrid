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

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>スケーリング・アライメントの集約ビュー。</summary>
    public ImageCharacteristics Characteristics => new(ScalingMode, Alignment);
}
