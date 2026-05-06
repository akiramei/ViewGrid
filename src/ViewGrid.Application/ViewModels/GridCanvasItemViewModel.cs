using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.ViewModels;

public sealed partial class GridCanvasItemViewModel : ObservableObject
{
    public Guid GridId { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    public int Rows { get; }
    public int Cols { get; }

    /// <summary>
    /// キャンバス幅 (px)。 配置タブの右ペインから編集される (LostFocus / Enter で
    /// <see cref="GridCanvasListViewModel.UpdateSelectedCanvasSizeAsync"/> 経由で永続化)。
    /// 変更で <see cref="CanvasSizeLabel"/> を再評価するため <see cref="ObservableProperty"/>。
    /// </summary>
    [ObservableProperty]
    public partial int CanvasWidth { get; set; }

    /// <summary>キャンバス高さ (px)。 <see cref="CanvasWidth"/> と同じ意味論。</summary>
    [ObservableProperty]
    public partial int CanvasHeight { get; set; }

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

    // ─── 右ペイン GridPropertiesView 用の編集ドラフト ───
    // CopyProperties / PlacementInspector と同じ 「ドラフト編集 → IsDirty → 保存ボタン」 パターン。
    // 編集中はここだけが書き換わり、 永続化済みの Name / CanvasWidth / CanvasHeight は保存ボタン
    // 押下 (= GridCanvasListViewModel.CommitEditingAsync) で初めて更新される。

    /// <summary>編集中のグリッド名 (保存前のドラフト)。</summary>
    [ObservableProperty]
    public partial string EditingName { get; set; } = string.Empty;

    /// <summary>編集中のキャンバス幅 px (保存前のドラフト)。</summary>
    [ObservableProperty]
    public partial int EditingCanvasWidth { get; set; }

    /// <summary>編集中のキャンバス高さ px (保存前のドラフト)。</summary>
    [ObservableProperty]
    public partial int EditingCanvasHeight { get; set; }

    /// <summary>
    /// ドラフトと永続化済み値の差分。 保存ボタンの IsEnabled 制御に使う。
    /// EditingName は Trim して比較する (前後空白だけの差は dirty にしない)。
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    partial void OnEditingNameChanged(string value) => RecomputeIsDirty();
    partial void OnEditingCanvasWidthChanged(int value) => RecomputeIsDirty();
    partial void OnEditingCanvasHeightChanged(int value) => RecomputeIsDirty();

    partial void OnNameChanged(string value)
    {
        // Source → Draft 同期: 保存完了後 / 外部からの再ロード時はドラフトも追従。
        // Editing 値が既に同値の場合 OnEditingNameChanged は呼ばれないので、
        // ここから明示的に IsDirty を再評価する (保存後の IsDirty=false 確定経路)。
        EditingName = value;
        RecomputeIsDirty();
    }

    partial void OnCanvasWidthChanged(int value)
    {
        EditingCanvasWidth = value;
        OnPropertyChanged(nameof(CanvasSizeLabel));
        RecomputeIsDirty();
    }

    partial void OnCanvasHeightChanged(int value)
    {
        EditingCanvasHeight = value;
        OnPropertyChanged(nameof(CanvasSizeLabel));
        RecomputeIsDirty();
    }

    private void RecomputeIsDirty()
    {
        IsDirty = (EditingName?.Trim() ?? string.Empty) != Name
            || EditingCanvasWidth != CanvasWidth
            || EditingCanvasHeight != CanvasHeight;
    }

    /// <summary>
    /// ドラフトを永続化済み値に戻す (リセットボタン / Esc キャンセル用)。
    /// 保存完了後の defensive sync にも使う (CommitEditingAsync 末尾)。 既に同値の場合
    /// <see cref="OnEditingNameChanged"/> 等の partial method がスキップされて IsDirty が
    /// 古いまま残らないよう、 末尾で明示的に <see cref="RecomputeIsDirty"/> を呼ぶ。
    /// </summary>
    public void RevertEditing()
    {
        EditingName = Name;
        EditingCanvasWidth = CanvasWidth;
        EditingCanvasHeight = CanvasHeight;
        RecomputeIsDirty();
    }

    public GridCanvasItemViewModel(GridCanvas grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        GridId = grid.Id;
        Name = grid.Name;
        Rows = grid.GridRows;
        Cols = grid.GridCols;
        CanvasWidth = grid.CanvasSize.Width;
        CanvasHeight = grid.CanvasSize.Height;
        // ドラフト初期値は永続化済み値と同じに揃える (IsDirty=false 状態)
        EditingName = grid.Name;
        EditingCanvasWidth = grid.CanvasSize.Width;
        EditingCanvasHeight = grid.CanvasSize.Height;
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
