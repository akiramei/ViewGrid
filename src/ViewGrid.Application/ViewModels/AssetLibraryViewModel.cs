using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// アセットライブラリ（準備フェーズの画像一覧）。
/// ファイル取り込み・削除・一覧更新の UI 状態を管理する。
/// </summary>
public sealed partial class AssetLibraryViewModel : ViewModelBase
{
    private readonly ImportImageUseCase _importUseCase;
    private readonly DeleteImageAssetUseCase _deleteUseCase;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IFilePickerService _filePickerService;
    private readonly IMessenger _messenger;
    private readonly IUndoRedoService _history;
    private readonly ILocalizationService _loc;
    private readonly ILogger<AssetLibraryViewModel> _logger;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial AssetItemViewModel? SelectedAsset { get; set; }

    public ObservableCollection<AssetItemViewModel> Assets { get; } = [];

    /// <summary>
    /// マルチセレクト中のアセット集合。Ctrl/Shift+クリックで複数選択された要素が入る。
    /// View 側 (<c>AssetLibraryView</c>) の <c>SelectionChanged</c> で
    /// <see cref="UpdateSelectedAssets"/> を経由して同期される。
    /// 削除は本コレクション全件を対象とし、件数 ≧ 2 のときは特性編集パネルが disabled になる。
    /// </summary>
    public ObservableCollection<AssetItemViewModel> SelectedAssets { get; } = [];

    /// <summary>
    /// <see cref="UpdateSelectedAssets"/> の Clear/Add 連鎖中は CollectionChanged で
    /// 中間状態の通知が走らないよう抑止し、完了時に 1 回だけ通知する。
    /// EF Core の DbContext は同時クエリをサポートしないため、再入抑制が必須。
    /// </summary>
    private bool _bulkUpdatingSelection;

    public int SelectedCount => SelectedAssets.Count;
    public bool IsMultiSelected => SelectedAssets.Count > 1;

    public AssetLibraryViewModel(
        ImportImageUseCase importUseCase,
        DeleteImageAssetUseCase deleteUseCase,
        IImageAssetRepository assetRepository,
        IThumbnailService thumbnailService,
        IFilePickerService filePickerService,
        IMessenger messenger,
        IUndoRedoService history,
        ILocalizationService loc,
        ILogger<AssetLibraryViewModel> logger)
    {
        _importUseCase = importUseCase;
        _deleteUseCase = deleteUseCase;
        _assetRepository = assetRepository;
        _thumbnailService = thumbnailService;
        _filePickerService = filePickerService;
        _messenger = messenger;
        _history = history;
        _loc = loc;
        _logger = logger;

        SelectedAssets.CollectionChanged += (_, _) =>
        {
            if (_bulkUpdatingSelection) return; // bulk 完了時に明示的に通知する
            NotifySelectionChanged();
        };
    }

    /// <summary>
    /// View からの選択変更を 1 回の操作として反映する。Clear と Add の連鎖で発火する
    /// CollectionChanged を抑止し、完了時に 1 回だけ通知する（再入による多重ロード回避）。
    /// </summary>
    public void UpdateSelectedAssets(IReadOnlyList<AssetItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _bulkUpdatingSelection = true;
        try
        {
            SelectedAssets.Clear();
            foreach (var item in items)
                SelectedAssets.Add(item);
        }
        finally
        {
            _bulkUpdatingSelection = false;
        }
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedAssets));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsMultiSelected));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => DeleteSelectedCommand.NotifyCanExecuteChanged();
    partial void OnSelectedAssetChanged(AssetItemViewModel? value) => DeleteSelectedCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = null;

            var assets = await _assetRepository.FindAllAsync(ct);
            Assets.Clear();
            // 古いアセット参照が選択に残らないようにクリア
            UpdateSelectedAssets(Array.Empty<AssetItemViewModel>());
            SelectedAsset = null;
            foreach (var asset in assets)
            {
                Assets.Add(BuildItem(asset));
            }

            LogLoaded(_logger, assets.Count);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task PickFilesAndImportAsync(CancellationToken ct = default)
    {
        var paths = await _filePickerService.PickImagesAsync(ct);
        if (paths.Count == 0)
            return;

        await AddFilesAsync(paths, ct);
    }

    /// <summary>
    /// D&amp;D 経由で呼ぶためのエントリポイント。複数ファイルを順に取り込み、
    /// 既存（重複）は再読み込みで反映する。
    /// </summary>
    public async Task AddFilesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
            return;

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var imported = 0;
            var duplicated = 0;
            var failed = 0;

            foreach (var path in paths)
            {
                if (ct.IsCancellationRequested)
                    break;

                var result = await _importUseCase.ExecuteAsync(
                    new ImportImageRequest { SourcePath = path, SourceType = ImageSource.File },
                    ct);

                if (result.IsError)
                {
                    failed++;
                    LogImportFailed(_logger, path, string.Join(", ", result.Errors));
                    continue;
                }

                if (result.Value.WasDuplicate)
                    duplicated++;
                else
                    imported++;
            }

            StatusMessage = BuildStatus(imported, duplicated, failed);
            await ReloadAssetsAsync(ct);

            // 取り込みは ImportImageUseCase が既定 Copy を自動作成するため、
            // 候補ライブラリにも変更が及ぶ。失敗のみのケースでは通知不要。
            if (imported > 0 || duplicated > 0)
            {
                // アセット追加は Undo 対象外。新規 Copy が生まれるため履歴の参照整合性が崩れる前にクリア。
                _history.Clear();
                _messenger.Send(new CopyLibraryChangedMessage());
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        // SelectedAssets が空のときは SelectedAsset 単独でも削除できるよう fallback
        var targets = SelectedAssets.Count > 0
            ? SelectedAssets.ToList()
            : SelectedAsset is { } single ? [single] : new List<AssetItemViewModel>();
        if (targets.Count == 0 || IsBusy)
            return;

        try
        {
            IsBusy = true;
            var success = 0;
            var failed = 0;
            var errors = new List<string>();
            foreach (var target in targets)
            {
                if (ct.IsCancellationRequested) break;
                var result = await _deleteUseCase.ExecuteAsync(target.AssetId, ct);
                if (result.IsError)
                {
                    failed++;
                    errors.AddRange(result.Errors.Select(e => e.Description));
                    continue;
                }
                Assets.Remove(target);
                success++;
            }

            SelectedAssets.Clear();
            SelectedAsset = null;

            StatusMessage = failed == 0
                ? (success == 1
                    ? _loc.Format("Status_AssetDeletedSingleFmt", targets[0].DisplayName)
                    : _loc.Format("Status_AssetDeletedCountFmt", success))
                : _loc.Format("Status_AssetDeletedMixedFmt", success, failed, string.Join(", ", errors.Distinct()));

            // Asset 削除は cascade で関連 ImageCopy も削除されるため候補にも反映。
            // 履歴に該当 Copy を参照する Command が残ると Undo で NotFound になるので全消去。
            if (success > 0)
            {
                _history.Clear();
                _messenger.Send(new CopyLibraryChangedMessage());
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDeleteSelected() => !IsBusy && (SelectedAssets.Count > 0 || SelectedAsset is not null);

    /// <summary>
    /// 単一の Asset を Id 指定で削除する。配置タブの候補ツリーから右クリックメニュー経由で
    /// 呼ばれる経路用。<see cref="DeleteSelectedAsync"/> と異なり Selection 状態を介さない。
    /// Cascade で関連 ImageCopy / GridPlacement も DB から消える。
    /// </summary>
    public async Task<bool> DeleteByIdAsync(Guid assetId, CancellationToken ct = default)
    {
        if (IsBusy) return false;
        try
        {
            IsBusy = true;
            var target = Assets.FirstOrDefault(a => a.AssetId == assetId);
            var displayName = target?.DisplayName;

            var result = await _deleteUseCase.ExecuteAsync(assetId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                return false;
            }

            if (target is not null)
            {
                Assets.Remove(target);
                if (ReferenceEquals(SelectedAsset, target)) SelectedAsset = null;
                SelectedAssets.Remove(target);
            }

            StatusMessage = displayName is null
                ? _loc["Status_AssetDeleted"]
                : _loc.Format("Status_AssetDeletedSingleFmt", displayName);

            // Asset 削除は cascade で関連 ImageCopy も削除されるため候補にも反映。
            // 履歴に該当 Copy を参照する Command が残ると Undo で NotFound になるので全消去。
            _history.Clear();
            _messenger.Send(new CopyLibraryChangedMessage());
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAssetsAsync(CancellationToken ct)
    {
        var assets = await _assetRepository.FindAllAsync(ct);
        Assets.Clear();
        // 古いアセット参照が選択に残らないようにクリア
        UpdateSelectedAssets(Array.Empty<AssetItemViewModel>());
        SelectedAsset = null;
        foreach (var asset in assets)
            Assets.Add(BuildItem(asset));
    }

    private AssetItemViewModel BuildItem(ImageAsset asset)
    {
        var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
        return new AssetItemViewModel(asset, thumb);
    }

    private string BuildStatus(int imported, int duplicated, int failed)
    {
        var parts = new List<string>();
        if (imported > 0) parts.Add(_loc.Format("Status_AssetImportedFmt", imported));
        if (duplicated > 0) parts.Add(_loc.Format("Status_AssetDuplicatedFmt", duplicated));
        if (failed > 0) parts.Add(_loc.Format("Status_AssetImportFailedFmt", failed));
        return parts.Count > 0 ? string.Join(" / ", parts) : _loc["Status_NoChange"];
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "アセット一覧を読み込み: {Count} 件")]
    private static partial void LogLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "画像取り込み失敗 path={Path}: {Errors}")]
    private static partial void LogImportFailed(ILogger logger, string path, string errors);
}
