using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 選択中アセットに紐づく論理コピー一覧を管理する。
/// アセット選択の変更は外部（MainWindowViewModel）から LoadForAssetAsync 経由で通知する。
/// </summary>
public sealed partial class CopyListViewModel : ViewModelBase
{
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly CreateLogicalCopyUseCase _createUseCase;
    private readonly IMessenger _messenger;
    private readonly IUndoRedoService _history;
    private readonly ILogger<CopyListViewModel> _logger;

    private Guid? _currentAssetId;

    [ObservableProperty]
    public partial CopyItemViewModel? SelectedCopy { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ObservableCollection<CopyItemViewModel> Copies { get; } = [];

    /// <summary>
    /// マルチセレクト中の論理コピー集合。Ctrl/Shift+クリックで複数選択された要素が入る。
    /// View 側 (<c>CopyListView</c>) の <c>SelectionChanged</c> で
    /// <see cref="UpdateSelectedCopies"/> を経由して同期される。
    /// 削除は本コレクション全件を対象とし、件数 ≧ 2 のときは特性編集パネルが disabled になる。
    /// </summary>
    public ObservableCollection<CopyItemViewModel> SelectedCopies { get; } = [];

    private bool _bulkUpdatingSelection;

    public int SelectedCount => SelectedCopies.Count;
    public bool IsMultiSelected => SelectedCopies.Count > 1;

    public bool HasAsset => _currentAssetId.HasValue;

    public CopyListViewModel(
        IImageCopyRepository copyRepository,
        IImageAssetRepository assetRepository,
        IThumbnailService thumbnailService,
        CreateLogicalCopyUseCase createUseCase,
        IMessenger messenger,
        IUndoRedoService history,
        ILogger<CopyListViewModel> logger)
    {
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _thumbnailService = thumbnailService;
        _createUseCase = createUseCase;
        _messenger = messenger;
        _history = history;
        _logger = logger;

        SelectedCopies.CollectionChanged += (_, _) =>
        {
            if (_bulkUpdatingSelection) return;
            NotifySelectionChanged();
        };
    }

    /// <summary>
    /// View からの選択変更を 1 回の操作として反映する。Clear と Add の連鎖で発火する
    /// CollectionChanged を抑止し、完了時に 1 回だけ通知する。
    /// </summary>
    public void UpdateSelectedCopies(IReadOnlyList<CopyItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _bulkUpdatingSelection = true;
        try
        {
            SelectedCopies.Clear();
            foreach (var item in items)
                SelectedCopies.Add(item);
        }
        finally
        {
            _bulkUpdatingSelection = false;
        }
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCopies));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsMultiSelected));
        DeleteSelectedCopyCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => DeleteSelectedCopyCommand.NotifyCanExecuteChanged();
    partial void OnSelectedCopyChanged(CopyItemViewModel? value) => DeleteSelectedCopyCommand.NotifyCanExecuteChanged();

    public async Task LoadForAssetAsync(Guid? assetId, CancellationToken ct = default)
    {
        _currentAssetId = assetId;
        OnPropertyChanged(nameof(HasAsset));

        Copies.Clear();
        // 旧アセットの選択が残ると DeleteSelectedCopyAsync 等が Copies に存在しない
        // CopyId を対象にしてしまうため、ここで明示的にクリアする
        UpdateSelectedCopies(Array.Empty<CopyItemViewModel>());
        SelectedCopy = null;
        StatusMessage = null;

        if (assetId is null)
            return;

        try
        {
            IsBusy = true;
            // AutoCrop の画像クリックピッカー UI で使うサムネパスを事前解決
            var asset = await _assetRepository.FindByIdAsync(assetId.Value, ct);
            var thumbnailPath = asset is null ? null : _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var copies = await _copyRepository.FindByAssetIdAsync(assetId.Value, ct);
            foreach (var copy in copies)
                Copies.Add(new CopyItemViewModel(copy, thumbnailPath));

            SelectedCopy = Copies.FirstOrDefault();
            LogLoaded(_logger, assetId.Value, copies.Count);
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
    public async Task CreateCopyAsync(CancellationToken ct = default)
    {
        if (_currentAssetId is null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            var ordinal = Copies.Count + 1;
            var result = await _createUseCase.ExecuteAsync(
                _currentAssetId.Value,
                copyName: $"コピー {ordinal}",
                ct: ct);

            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            var asset = await _assetRepository.FindByIdAsync(_currentAssetId.Value, ct);
            var thumbnailPath = asset is null ? null : _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var item = new CopyItemViewModel(result.Value, thumbnailPath);
            Copies.Add(item);
            SelectedCopy = item;
            StatusMessage = $"「{item.DisplayName}」を作成しました。";
            // 新規 Copy 作成は Undo 対象外（履歴に積めない）。既存履歴の整合を保つため全消去。
            _history.Clear();
            _messenger.Send(new CopyLibraryChangedMessage());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    public async Task DeleteSelectedCopyAsync(CancellationToken ct = default)
    {
        var targets = SelectedCopies.Count > 0
            ? SelectedCopies.ToList()
            : SelectedCopy is { } single ? [single] : new List<CopyItemViewModel>();
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
                var result = await _copyRepository.DeleteAsync(target.CopyId, ct);
                if (result.IsError)
                {
                    failed++;
                    errors.AddRange(result.Errors.Select(e => e.Description));
                    continue;
                }
                Copies.Remove(target);
                success++;
            }

            SelectedCopies.Clear();
            SelectedCopy = Copies.FirstOrDefault();

            StatusMessage = failed == 0
                ? (success == 1 ? $"「{targets[0].DisplayName}」を削除しました。" : $"{success} 件削除しました。")
                : $"{success} 件削除、{failed} 件失敗（{string.Join(", ", errors.Distinct())}）";
            // Copy 削除は cascade で関連 Placement も削除されるため履歴を全消去
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

    private bool CanDeleteSelected() => !IsBusy && (SelectedCopies.Count > 0 || SelectedCopy is not null);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "論理コピー一覧を読み込み: asset={AssetId} count={Count}")]
    private static partial void LogLoaded(ILogger logger, Guid assetId, int count);
}
