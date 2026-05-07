using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.AutoSave;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブで使うグリッドキャンバス一覧。Create/Delete/Rename を提供。
/// 新規作成フォームはフライアウト展開用に VM 内で直接保持する。
/// 「最後に開いていたグリッド」 を <see cref="IAppSettingsService"/> 経由で永続化し、
/// 起動時に復元する。
/// </summary>
public sealed partial class GridCanvasListViewModel : ViewModelBase, IDisposable
{
    /// <summary>auto-save の debounce 時間。 SelectedGrid.IsDirty 立ち上がりからこの時間静止すれば 1 回だけ保存。</summary>
    internal static readonly TimeSpan AutoSaveDebounce = TimeSpan.FromMilliseconds(1000);

    private readonly IGridCanvasRepository _repository;
    private readonly CreateGridCanvasUseCase _createUseCase;
    private readonly DeleteGridCanvasUseCase _deleteUseCase;
    private readonly RenameGridCanvasUseCase _renameUseCase;
    private readonly UpdateGridCanvasSizeUseCase _updateCanvasSizeUseCase;
    private readonly IAppSettingsService _appSettings;
    private readonly IUndoRedoService _history;
    private readonly ILogger<GridCanvasListViewModel> _logger;

    private readonly SaveCoordinator _autoSave;
    /// <summary>
    /// auto-save の対象アイテム。 IsDirty=true 検知時にセットし、 Coordinator の saveAction からこれを保存する。
    /// SelectedGrid 切替で <see cref="SelectedGrid"/> が変わっても、 旧アイテムが正しく保存されるようにフィールドで保持。
    /// </summary>
    private GridCanvasItemViewModel? _autoSaveTarget;
    /// <summary>
    /// SelectedGrid 切替時、 旧アイテムの flush を退避する Task。 外部 (MainWindowVM) が
    /// 切替後の LoadGridAsync 起動前に <see cref="WaitPendingSelectedGridFlushAsync"/> で待てる。
    /// </summary>
    private Task _pendingSelectedGridFlushTask = Task.CompletedTask;
    private bool _disposed;

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

    /// <summary>
    /// 書き込み系コマンド (Create / Delete / Rename / UpdateCanvasSize / CommitEditing) の進行中フラグ。
    /// auto-save はこのフラグだけを尊重する (= 自分自身の重複起動を防ぎつつ、 内部 reload には邪魔されない)。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsSaving { get; set; }

    /// <summary>
    /// 読み込み系経路 (LoadAsync / RefreshAfterHistoryAsync) の進行中フラグ。 UI の ProgressBar
    /// 表示や 「読み込み中なので操作スキップ」 ガードに使う。 auto-save はこのフラグでは止まらない。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// View 互換のための合成プロパティ。 ProgressBar の IsVisible 等にバインドされている既存
    /// XAML を無傷に保つ。 内部ロジックは <see cref="IsSaving"/> / <see cref="IsLoading"/>
    /// を直接見るほうが意図が明確 (auto-save 競合制御を細粒度化するため)。
    /// </summary>
    public bool IsBusy => IsSaving || IsLoading;

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
        UpdateGridCanvasSizeUseCase updateCanvasSizeUseCase,
        IAppSettingsService appSettings,
        IUndoRedoService history,
        ILogger<GridCanvasListViewModel> logger)
    {
        _repository = repository;
        _createUseCase = createUseCase;
        _deleteUseCase = deleteUseCase;
        _renameUseCase = renameUseCase;
        _updateCanvasSizeUseCase = updateCanvasSizeUseCase;
        _appSettings = appSettings;
        _history = history;
        _logger = logger;
        _autoSave = new SaveCoordinator(
            AutoSaveDebounce,
            isEnabled: () => _appSettings.Current.EnableAutoSave,
            isDirty: () => _autoSaveTarget?.IsDirty == true,
            signatureProvider: () => _autoSaveTarget is { } t ? ComputeAutoSaveSignature(t) : string.Empty,
            saveAction: ct => _autoSaveTarget is { } t ? TryCommitEditingForAsync(t, ct) : Task.FromResult(true));
        _appSettings.Changed += OnAppSettingsChanged;
    }

    private void OnAppSettingsChanged(object? sender, AppSettings settings)
    {
        if (!settings.EnableAutoSave) _autoSave.Cancel();
    }

    private static string ComputeAutoSaveSignature(GridCanvasItemViewModel target)
        => string.Concat(
            target.GridId, "|",
            target.EditingName ?? string.Empty, "|",
            target.EditingCanvasWidth, "|",
            target.EditingCanvasHeight);

    /// <summary>
    /// 保留中の auto-save (旧 SelectedGrid 切替時のもの含む) が完了するまで待つ。
    /// MainWindowVM が SelectedGrid 切替 → LoadGridAsync 起動前に await する。
    /// </summary>
    public Task WaitPendingSelectedGridFlushAsync() => _pendingSelectedGridFlushTask;

    /// <summary>
    /// 保留中の auto-save を即実行 + その完了を待つ。 アプリ終了時の <c>FlushAllAutoSavesAsync</c> から呼ばれる。
    /// </summary>
    public Task FlushAutoSaveAsync(CancellationToken ct = default) => _autoSave.FlushAsync(ct);

    /// <summary>
    /// 監視中アイテムの編集系プロパティ (Editing*) や IsDirty が変化したら、 dirty 状態のアイテムを
    /// <see cref="_autoSaveTarget"/> に確定して Coordinator に通知する。 Coordinator 側で設定 OFF /
    /// 同 signature 失敗 / dirty なし は内部 gate でスキップ。 連続編集 (例: 文字を続けて入力)
    /// でも debounce が毎回リセットされる。
    /// </summary>
    private void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (sender is not GridCanvasItemViewModel item) return;
        // 編集系プロパティか IsDirty 変化のみ対象。 他のプロパティ (Name 等の永続化値同期) はスキップ。
        if (e.PropertyName is not (
            nameof(GridCanvasItemViewModel.EditingName)
            or nameof(GridCanvasItemViewModel.EditingCanvasWidth)
            or nameof(GridCanvasItemViewModel.EditingCanvasHeight)
            or nameof(GridCanvasItemViewModel.IsDirty)))
            return;

        // _autoSaveTarget は Coordinator の signature/save delegate が読むので、
        // NotifyEdited より前にセットして最新値で評価される状態を作る。
        _autoSaveTarget = item;
        _autoSave.NotifyEdited();
    }

    /// <summary>
    /// SelectedGrid が変わるたびに LastOpenedGridId を settings に保存する。
    /// 起動時の自動復元で同じ値を再保存しないよう <see cref="_suppressLastOpenedSave"/> でガード。
    /// 失敗 (権限不足等) は静かに飲む — 起動時の選択復元が出来ない程度の影響にとどまる。
    /// 並行更新の race は <see cref="IAppSettingsService.UpdateAsync"/> 側の lock で吸収されるため、
    /// VM 側では fire-and-forget で良い (テストや確実な永続化待ちは <see cref="LastOpenedSaveTask"/>)。
    /// </summary>
    /// <summary>
    /// 旧 SelectedGrid の購読解除 + 旧アイテムが dirty なら flush 予約。
    /// auto-save ON のときは、 dispatcher 経由の flush で取りこぼしが起きるケース
    /// (例: auto-save OFF 中に edit → ON 化 → 切替 で <see cref="_autoSaveTarget"/> 未設定 +
    /// dispatcher にタイマー無し) のために、 flush 後に <see cref="TryCommitEditingForAsync"/>
    /// での直接 commit も試みる (Codex P2 指摘)。
    /// auto-save OFF のときは既存仕様どおり、 切替時には保存しない (ユーザーが手動保存する設計を維持)。
    /// </summary>
    partial void OnSelectedGridChanging(GridCanvasItemViewModel? oldValue, GridCanvasItemViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnSelectedItemPropertyChanged;

        if (oldValue is { IsDirty: true } && _appSettings.Current.EnableAutoSave)
        {
            _pendingSelectedGridFlushTask = FlushAndCommitOnSwitchAsync(oldValue);
        }
    }

    /// <summary>
    /// 旧 grid の dirty edit を確実に保存する。 Coordinator の保留中タイマーを先に flush し、
    /// それで保存されなかった (target 不一致 / タイマー無し等の) 場合に備えて直接 commit を試みる。
    /// 既に保存済みなら 2 段目の <see cref="TryCommitEditingForAsync"/> は IsDirty=false で即 return。
    /// IsLoading 中 (reload 経路) でも IsSaving とは独立 gate なので edit はロストしない
    /// (Phase B-2 で <c>allowDuringBusy</c> workaround を撤去)。
    /// </summary>
    private async Task FlushAndCommitOnSwitchAsync(GridCanvasItemViewModel target)
    {
        try { await _autoSave.FlushAsync(CancellationToken.None); }
        catch { /* StatusMessage に反映済み想定。 続行する */ }

        if (target.IsDirty)
        {
            try { await TryCommitEditingForAsync(target, CancellationToken.None); }
            catch { /* 同上 */ }
        }
    }

    partial void OnSelectedGridChanged(GridCanvasItemViewModel? value)
    {
        if (value is not null)
            value.PropertyChanged += OnSelectedItemPropertyChanged;

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
        if (IsLoading) return;
        try
        {
            IsLoading = true;

            // mid-session reload (RefreshAfterHistoryAsync 等から) で in-memory の現在選択を
            // 維持するために、 reload 前に SelectedGrid.GridId を捕捉する。 持続中の
            // LastOpenedGridId save が完了する前にここに来た場合、 _appSettings.Current は
            // 古い id のままなので、 settings 値を見ると意図しないグリッドに切り戻る (Codex review P2)。
            var currentSelectedId = SelectedGrid?.GridId;

            await ReloadGridsInternalAsync(ct);

            // 復元優先順:
            // (1) reload 前の SelectedGrid (mid-session で 「今見ている grid」 を保持する)、
            // (2) AppSettings.LastOpenedGridId (起動直後 / 初回ロード時の復元)、
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
        finally { IsLoading = false; }
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
        if (IsSaving || IsLoading) return;
        try
        {
            IsSaving = true;

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
        finally { IsSaving = false; }
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsSaving || IsLoading) return;
        try
        {
            IsSaving = true;
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
        finally { IsSaving = false; }
    }

    [RelayCommand]
    public async Task RenameSelectedAsync(string newName, CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsSaving || IsLoading) return;
        if (string.IsNullOrWhiteSpace(newName)) return;
        var trimmed = newName.Trim();
        if (trimmed == selected.Name) return;

        try
        {
            IsSaving = true;
            var ok = await RenameInternalAsync(selected, trimmed, ct);
            if (ok) StatusMessage = "名前を変更しました。";
        }
        finally { IsSaving = false; }
    }

    /// <summary>
    /// 選択中グリッドの CanvasSize を更新する。 配置 (CellPosition / OccupySize) や重みは
    /// 変更しないので、 視覚的にはセルが等倍で拡縮されるだけ。 Undo/Redo は
    /// <see cref="UpdateGridCanvasSizeCommand"/> 経由。 同サイズなら no-op。
    /// </summary>
    [RelayCommand]
    public async Task UpdateSelectedCanvasSizeAsync(PixelSize newSize, CancellationToken ct = default)
    {
        var selected = SelectedGrid;
        if (selected is null || IsSaving || IsLoading) return;

        var before = new PixelSize(selected.CanvasWidth, selected.CanvasHeight);
        if (before == newSize) return;

        try
        {
            IsSaving = true;
            var ok = await UpdateCanvasSizeInternalAsync(selected, before, newSize, ct);
            if (ok) StatusMessage = "キャンバスサイズを変更しました。";
        }
        finally { IsSaving = false; }
    }

    /// <summary>
    /// 右ペイン GridPropertiesView の保存ボタンから呼ばれる。 ドラフト (EditingName / EditingCanvas*)
    /// を読み、 永続化済み値と異なるものだけを順に Rename / UpdateCanvasSize で永続化する。
    /// 各操作は独立した履歴エントリとして積まれる (両方変更時は Undo 2 回で元に戻る)。
    /// 履歴は CopyProperties / PlacementInspector の 「保存ボタン経由」 パターンと同じ意味論。
    /// auto-save 経路は <see cref="TryCommitEditingForAsync"/> を直接呼ぶ (旧 SelectedGrid を引数で受け渡せるように)。
    /// </summary>
    [RelayCommand]
    public async Task CommitEditingAsync(CancellationToken ct = default)
        => _ = await TryCommitEditingForAsync(SelectedGrid, ct);

    /// <summary>
    /// 指定 <paramref name="target"/> の編集ドラフトを保存する。 成功 (no-op 含む) で <c>true</c>、 失敗で <c>false</c>。
    /// auto-save 経路は「dirty 検知時の対象アイテム」 を引数で受け渡すことで、
    /// SelectedGrid 切替後でも旧アイテムを正しく保存できるようにしている (race 防止)。
    /// <see cref="IsSaving"/> は本メソッド自身の重複起動だけを抑止し、 <see cref="IsLoading"/>
    /// (reload 経路) との競合は内部 EF DbContext 同時クエリ問題なので別経路 (Phase C) で解決する想定。
    /// </summary>
    internal async Task<bool> TryCommitEditingForAsync(GridCanvasItemViewModel? target, CancellationToken ct = default)
    {
        if (target is null || !target.IsDirty) return true;
        if (IsSaving) return true;

        var newName = target.EditingName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "グリッド名を入力してください。";
            return false;
        }

        var nameChanged = newName != target.Name;
        var newWidth = target.EditingCanvasWidth;
        var newHeight = target.EditingCanvasHeight;
        var sizeChanged = newWidth != target.CanvasWidth || newHeight != target.CanvasHeight;

        try
        {
            IsSaving = true;
            var savedAny = false;
            if (nameChanged)
            {
                var ok = await RenameInternalAsync(target, newName, ct);
                if (!ok) return false;
                savedAny = true;
            }
            if (sizeChanged)
            {
                var before = new PixelSize(target.CanvasWidth, target.CanvasHeight);
                var after = new PixelSize(newWidth, newHeight);
                var ok = await UpdateCanvasSizeInternalAsync(target, before, after, ct);
                if (!ok) return false;
                savedAny = true;
            }
            if (savedAny)
            {
                // 保存完了後の defensive sync: RenameInternalAsync / UpdateCanvasSizeInternalAsync
                // 内で target.Name / CanvasWidth / CanvasHeight を更新すると OnNameChanged 等の
                // partial method 経由で Editing も同期されるが、 同値スキップやタイミング差で
                // IsDirty=true が残る稀なケースを防ぐため、 ここで RevertEditing で確実に揃える
                // (Editing は最新の永続化値と一致 → IsDirty=false)。
                target.RevertEditing();
                StatusMessage = "グリッド情報を保存しました。";
            }
            return true;
        }
        finally { IsSaving = false; }
    }

    /// <summary>右ペインのリセットボタンから呼ばれる。 ドラフトを永続化済み値で上書き。</summary>
    [RelayCommand]
    public void RevertEditing() => SelectedGrid?.RevertEditing();

    /// <summary>
    /// <see cref="RenameSelectedAsync"/> と <see cref="CommitEditingAsync"/> の共通実装。
    /// 履歴 ExecuteAsync + selected.Name 反映までを行い、 IsBusy / StatusMessage は呼び出し元で制御。
    /// </summary>
    private async Task<bool> RenameInternalAsync(
        GridCanvasItemViewModel selected, string newName, CancellationToken ct)
    {
        var description = $"リネーム: 「{selected.Name}」→「{newName}」";
        var command = new RenameGridCanvasCommand(
            _renameUseCase, selected.GridId, selected.Name, newName, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }
        selected.Name = newName;
        return true;
    }

    /// <summary>
    /// <see cref="UpdateSelectedCanvasSizeAsync"/> と <see cref="CommitEditingAsync"/> の共通実装。
    /// 履歴 ExecuteAsync + selected.CanvasWidth / Height 反映までを行う。
    /// </summary>
    private async Task<bool> UpdateCanvasSizeInternalAsync(
        GridCanvasItemViewModel selected, PixelSize before, PixelSize after, CancellationToken ct)
    {
        var description = $"キャンバスサイズ: {before.Width}×{before.Height} → {after.Width}×{after.Height} px";
        var command = new UpdateGridCanvasSizeCommand(
            _updateCanvasSizeUseCase, selected.GridId, before, after, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }
        selected.CanvasWidth = after.Width;
        selected.CanvasHeight = after.Height;
        return true;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _appSettings.Changed -= OnAppSettingsChanged;
        if (SelectedGrid is not null)
            SelectedGrid.PropertyChanged -= OnSelectedItemPropertyChanged;
        _autoSave.Dispose();
    }
}
