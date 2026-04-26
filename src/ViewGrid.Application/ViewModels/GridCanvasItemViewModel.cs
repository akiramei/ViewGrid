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

    /// <summary>
    /// 各列のロック状態（要素数 = <see cref="Cols"/>）。ロック中の列はフィット動作で
    /// 重みが変動しない。空配列なら全列アンロック扱い（旧グリッドとの後方互換）。
    /// </summary>
    [ObservableProperty]
    public partial ImmutableArray<bool> ColLocked { get; set; }

    /// <summary>各行のロック状態（要素数 = <see cref="Rows"/>）。</summary>
    [ObservableProperty]
    public partial ImmutableArray<bool> RowLocked { get; set; }

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
        // 旧 DB の grid だと空配列の可能性あり。AllUnlocked で要素数を揃える。
        ColLocked = grid.ColLocked.Length == grid.GridCols
            ? grid.ColLocked
            : GridCanvas.AllUnlocked(grid.GridCols);
        RowLocked = grid.RowLocked.Length == grid.GridRows
            ? grid.RowLocked
            : GridCanvas.AllUnlocked(grid.GridRows);
    }
}
