using System;

namespace ViewGrid.Core.Entities;

/// <summary>
/// グリッド上への画像配置。占有セル左上の位置 <see cref="Position"/> と、配置時の微調整
/// <see cref="PixelOffset"/> を保持する。
/// </summary>
public sealed class GridPlacement
{
    public required Guid Id { get; init; }
    public required Guid GridId { get; init; }
    public required Guid CopyId { get; init; }

    /// <summary>占有セルの左上位置（0 ベースのセル座標）。</summary>
    public required CellPosition Position { get; set; }

    /// <summary>配置時のピクセル単位微調整（将来拡張用、既定 0,0）。</summary>
    public int PixelOffsetX { get; set; }
    public int PixelOffsetY { get; set; }

    /// <summary>配置順序（重なり順制御に使用）。</summary>
    public required int PlacementOrder { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
