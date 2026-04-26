using System;
using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

public sealed partial class GridCanvasItemViewModel : ObservableObject
{
    public Guid GridId { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public int Rows { get; }
    public int Cols { get; }
    public int CanvasWidth { get; }
    public int CanvasHeight { get; }

    /// <summary>
    /// 各列の幅比率（要素数 = <see cref="Cols"/>）。
    /// 境界ドラッグなどで動的に書き換わる可能性があるため ObservableProperty。
    /// </summary>
    [ObservableProperty]
    public partial ImmutableArray<int> ColWeights { get; set; }

    /// <summary>各行の高さ比率（要素数 = <see cref="Rows"/>）。</summary>
    [ObservableProperty]
    public partial ImmutableArray<int> RowWeights { get; set; }

    public string GridSizeLabel => $"{Cols}×{Rows} セル";
    public string CanvasSizeLabel => $"{CanvasWidth}×{CanvasHeight} px";

    public GridCanvasItemViewModel(GridCanvas grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        GridId = grid.Id;
        Name = grid.Name;
        IsActive = grid.IsActive;
        Rows = grid.GridRows;
        Cols = grid.GridCols;
        CanvasWidth = grid.CanvasSize.Width;
        CanvasHeight = grid.CanvasSize.Height;
        ColWeights = grid.ColWeights;
        RowWeights = grid.RowWeights;
    }
}
