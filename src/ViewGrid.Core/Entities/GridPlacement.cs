using System;

namespace ViewGrid.Core.Entities;

/// <summary>
/// グリッド上への画像配置。占有セル左上の位置 <see cref="Position"/>、占有セルサイズ
/// <see cref="OccupySize"/>、配置時の微調整 <see cref="PixelOffsetX"/>/<see cref="PixelOffsetY"/>
/// を保持する。
/// <para>
/// <b>占有セル設計</b>: 配置単位で持つ（同じバリアントを別グリッドの別セルに配置しても、
/// それぞれ独立した OccupySize を選べる）。新規配置時は元バリアント
/// <see cref="ImageCopy.OccupySize"/> を初期値として継承するが、配置後はその配置のみで完結する。
/// </para>
/// </summary>
public sealed class GridPlacement
{
    public required Guid Id { get; init; }
    public required Guid GridId { get; init; }
    public required Guid CopyId { get; init; }

    /// <summary>占有セルの左上位置（0 ベースのセル座標）。</summary>
    public required CellPosition Position { get; set; }

    /// <summary>占有セルサイズ（NxM、矩形のみ）。配置単位で独立。</summary>
    public required OccupySize OccupySize { get; set; }

    /// <summary>配置時のピクセル単位微調整（将来拡張用、既定 0,0）。</summary>
    public int PixelOffsetX { get; set; }
    public int PixelOffsetY { get; set; }

    /// <summary>配置順序（重なり順制御に使用）。</summary>
    public required int PlacementOrder { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
