using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブで使うグリッドキャンバス一覧。Create/Delete/Rename/Activate を提供。
/// 新規作成フォームはフライアウト展開用に VM 内で直接保持する。
/// </summary>
public sealed partial class GridCanvasListViewModel : ViewModelBase
{
    private readonly IGridCanvasRepository _repository;
    private readonly CreateGridCanvasUseCase _createUseCase;
    private readonly DeleteGridCanvasUseCase _deleteUseCase;
    private readonly RenameGridCanvasUseCase _renameUseCase;
    private readonly SetActiveGridCanvasUseCase _setActiveUseCase;
    private readonly ILogger<GridCanvasListViewModel> _logger;

    public ObservableCollection<GridCanvasItemViewModel> Grids { get; } = [];

    [ObservableProperty]
    public partial GridCanvasItemViewModel? SelectedGrid { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    // 新規作成ドラフト
    [ObservableProperty] public partial bool IsCreating { get; set; }
    [ObservableProperty] public partial string DraftName { get; set; } = "新規グリッド";
    [ObservableProperty] public partial int DraftRows { get; set; } = 3;
    [ObservableProperty] public partial int DraftCols { get; set; } = 3;
    [ObservableProperty] public partial int DraftCanvasWidth { get; set; } = 1200;
    [ObservableProperty] public partial int DraftCanvasHeight { get; set; } = 1200;
    /// <summary>列比率（カンマ区切り、空 = 均等）。例: "2,1,1"。</summary>
    [ObservableProperty] public partial string DraftColWeights { get; set; } = string.Empty;
    /// <summary>行比率（カンマ区切り、空 = 均等）。例: "2,1,1"。</summary>
    [ObservableProperty] public partial string DraftRowWeights { get; set; } = string.Empty;

    public GridCanvasListViewModel(
        IGridCanvasRepository repository,
        CreateGridCanvasUseCase createUseCase,
        DeleteGridCanvasUseCase deleteUseCase,
        RenameGridCanvasUseCase renameUseCase,
        SetActiveGridCanvasUseCase setActiveUseCase,
        ILogger<GridCanvasListViewModel> logger)
    {
        _repository = repository;
        _createUseCase = createUseCase;
        _deleteUseCase = deleteUseCase;
        _renameUseCase = renameUseCase;
        _setActiveUseCase = setActiveUseCase;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await ReloadGridsInternalAsync(ct);
            SelectedGrid = Grids.FirstOrDefault(g => g.IsActive) ?? Grids.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; }
    }

    private async Task ReloadGridsInternalAsync(CancellationToken ct)
    {
        var all = await _repository.FindAllAsync(ct);
        Grids.Clear();
        foreach (var g in all)
            Grids.Add(new GridCanvasItemViewModel(g));
        LogLoaded(_logger, all.Count);
    }

    [RelayCommand]
    public void BeginCreate()
    {
        DraftName = $"グリッド {Grids.Count + 1}";
        DraftRows = 3;
        DraftCols = 3;
        DraftCanvasWidth = 1200;
        DraftCanvasHeight = 1200;
        DraftColWeights = string.Empty;
        DraftRowWeights = string.Empty;
        IsCreating = true;
        StatusMessage = null;
    }

    [RelayCommand]
    public void CancelCreate() => IsCreating = false;

    [RelayCommand]
    public async Task ConfirmCreateAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;

            // 比率テキスト → int 配列。空 / パース失敗時は null（=均等）。
            var parsedColWeights = ParseWeights(DraftColWeights);
            var parsedRowWeights = ParseWeights(DraftRowWeights);

            var result = await _createUseCase.ExecuteAsync(
                new CreateGridCanvasRequest
                {
                    Name = DraftName,
                    Rows = DraftRows,
                    Cols = DraftCols,
                    CanvasWidth = DraftCanvasWidth,
                    CanvasHeight = DraftCanvasHeight,
                    ColWeights = parsedColWeights,
                    RowWeights = parsedRowWeights,
                    SetAsActive = true,
                },
                ct);

            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            // アクティブ化した結果として他のフラグを落とす必要があるので、一覧再読込
            await ReloadGridsInternalAsync(ct);
            SelectedGrid = Grids.FirstOrDefault(g => g.GridId == result.Value.Id);
            IsCreating = false;
            StatusMessage = $"「{result.Value.Name}」を作成しました。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsBusy) return;
        try
        {
            IsBusy = true;
            var result = await _deleteUseCase.ExecuteAsync(selected.GridId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            Grids.Remove(selected);
            SelectedGrid = Grids.FirstOrDefault();
            StatusMessage = $"「{selected.Name}」を削除しました。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ActivateSelectedAsync(CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsBusy || selected.IsActive) return;
        try
        {
            IsBusy = true;
            var result = await _setActiveUseCase.ExecuteAsync(selected.GridId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            foreach (var g in Grids)
                g.IsActive = g.GridId == selected.GridId;

            StatusMessage = $"「{selected.Name}」をアクティブにしました。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RenameSelectedAsync(string newName, CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsBusy) return;
        try
        {
            IsBusy = true;
            var result = await _renameUseCase.ExecuteAsync(selected.GridId, newName, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }
            selected.Name = result.Value.Name;
            StatusMessage = "名前を変更しました。";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// "2,1,1" 形式の比率テキストを int 配列にパースする。空・パース失敗・要素 0 のときは null（均等扱い）。
    /// 検証は <see cref="CreateGridCanvasUseCase"/> 側で行うのでここでは構文だけ確認。
    /// </summary>
    private static int[]? ParseWeights(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var w)) return null;
            result[i] = w;
        }
        return result;
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "グリッド一覧を読み込み: {Count} 件")]
    private static partial void LogLoaded(ILogger logger, int count);
}
