using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブで使うグリッドキャンバス一覧。Create/Delete/Rename を提供。
/// 新規作成フォームはフライアウト展開用に VM 内で直接保持する。
/// 「最後に開いていたグリッド」 を <see cref="IAppSettingsService"/> 経由で永続化し、
/// 起動時に復元する (旧 「デフォルトグリッド」 (IsActive) UI 概念は廃止)。
/// </summary>
public sealed partial class GridCanvasListViewModel : ViewModelBase
{
    private readonly IGridCanvasRepository _repository;
    private readonly CreateGridCanvasUseCase _createUseCase;
    private readonly DeleteGridCanvasUseCase _deleteUseCase;
    private readonly RenameGridCanvasUseCase _renameUseCase;
    private readonly IAppSettingsService _appSettings;
    private readonly IUndoRedoService _history;
    private readonly ILogger<GridCanvasListViewModel> _logger;

    /// <summary>
    /// LoadAsync 中の SelectedGrid 自動選択 (LastOpenedGridId からの復元) で
    /// OnSelectedGridChanged が同じ値を settings に書き戻す無駄を防ぐためのフラグ。
    /// </summary>
    private bool _suppressLastOpenedSave;

    /// <summary>
    /// 直近の LastOpenedGridId 保存タスク。 OnSelectedGridChanged は fire-and-forget で
    /// settings.json を書き出すため、 テストや確実に永続化を待ちたい呼び出し元は
    /// このタスクを await する。 永続化失敗 (権限不足等) は静かに飲んでアプリ操作を妨げない。
    /// </summary>
    public Task LastOpenedSaveTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 最後に save 予約した LastOpenedGridId。 同期で読める <see cref="IAppSettingsService.Current"/>
    /// は pending save の途中だと古い値を返すため、 重複保存スキップ判定にはこちらを使う。
    /// </summary>
    private string? _lastQueuedLastOpenedId;

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
        IAppSettingsService appSettings,
        IUndoRedoService history,
        ILogger<GridCanvasListViewModel> logger)
    {
        _repository = repository;
        _createUseCase = createUseCase;
        _deleteUseCase = deleteUseCase;
        _renameUseCase = renameUseCase;
        _appSettings = appSettings;
        _history = history;
        _logger = logger;
    }

    /// <summary>
    /// SelectedGrid が変わるたびに LastOpenedGridId を settings に保存する。
    /// 起動時の自動復元で同じ値を再保存しないよう <see cref="_suppressLastOpenedSave"/> でガード。
    /// 失敗 (権限不足等) は静かに飲む — 起動時の選択復元が出来ない程度の影響にとどまる。
    /// 並行更新の race は <see cref="IAppSettingsService.UpdateAsync"/> 側の lock で吸収されるため、
    /// VM 側では fire-and-forget で良い (テストや確実な永続化待ちは <see cref="LastOpenedSaveTask"/>)。
    /// </summary>
    partial void OnSelectedGridChanged(GridCanvasItemViewModel? value)
    {
        if (_suppressLastOpenedSave) return;

        var newId = value?.GridId.ToString();
        if (_lastQueuedLastOpenedId == newId) return;

        _lastQueuedLastOpenedId = newId;
        // UpdateAsync 内で最新 Current を読み直して LastOpenedGridId だけ差し替えるため、
        // 他 VM が同時に Theme / AccentColor 等を変更していても巻き戻しは起きない。
        LastOpenedSaveTask = _appSettings.UpdateAsync(s => s with { LastOpenedGridId = newId });
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;

            // mid-session reload (RefreshAfterHistoryAsync 等から) で in-memory の現在選択を
            // 維持するために、 reload 前に SelectedGrid.GridId を捕捉する。 持続中の
            // LastOpenedGridId save が完了する前にここに来た場合、 _appSettings.Current は
            // 古い id のままなので、 settings 値を見ると意図しないグリッドに切り戻る (Codex review P2)。
            var currentSelectedId = SelectedGrid?.GridId;

            await ReloadGridsInternalAsync(ct);

            // 復元優先順:
            // (1) reload 前の SelectedGrid (mid-session で 「今見ている grid」 を保持する)、
            // (2) AppSettings.LastOpenedGridId (起動直後 / 初回ロード時の復元、 旧 IsActive 概念は廃止)、
            // (3) 先頭グリッド (fallback)。
            GridCanvasItemViewModel? restored = null;
            if (currentSelectedId is { } currentId)
                restored = Grids.FirstOrDefault(g => g.GridId == currentId);
            if (restored is null)
            {
                var lastId = _appSettings.Current.LastOpenedGridId;
                if (!string.IsNullOrWhiteSpace(lastId) && Guid.TryParse(lastId, out var parsed))
                    restored = Grids.FirstOrDefault(g => g.GridId == parsed);
            }

            // 復元時は OnSelectedGridChanged の無駄な settings 書き戻しを抑制する。
            _suppressLastOpenedSave = true;
            try
            {
                SelectedGrid = restored ?? Grids.FirstOrDefault();
                // 復元した id で _lastQueuedLastOpenedId を初期化することで、 次回 SelectedGrid が
                // 別グリッドに変わったときの重複判定が正しく効く (起動時 vs 手動切替の両経路を整合)。
                _lastQueuedLastOpenedId = SelectedGrid?.GridId.ToString();
            }
            finally { _suppressLastOpenedSave = false; }
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
                },
                ct);

            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            // 新規グリッド作成は Undo 対象外（cascade なしだが派生履歴を破綻させ得るため履歴破棄）
            _history.Clear();

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

            // グリッド削除は cascade で配置を消すため、関連する履歴は復元不能 → 全消去
            _history.Clear();

            Grids.Remove(selected);
            SelectedGrid = Grids.FirstOrDefault();
            StatusMessage = $"「{selected.Name}」を削除しました。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RenameSelectedAsync(string newName, CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsBusy) return;
        if (string.IsNullOrWhiteSpace(newName)) return;
        if (newName.Trim() == selected.Name) return;

        try
        {
            IsBusy = true;
            var trimmed = newName.Trim();
            var description = $"リネーム: 「{selected.Name}」→「{trimmed}」";
            var command = new RenameGridCanvasCommand(
                _renameUseCase, selected.GridId, selected.Name, trimmed, description);
            var result = await _history.ExecuteAsync(command, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }
            selected.Name = trimmed;
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
