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
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 選択中アセットに紐づくバリアント（旧称: 論理コピー）一覧を管理する。
/// アセット選択の変更は外部（MainWindowViewModel）から LoadForAssetAsync 経由で通知する。
/// </summary>
public sealed partial class CopyListViewModel : ViewModelBase
{
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IImageStorage _imageStorage;
    private readonly CreateLogicalCopyUseCase _createUseCase;
    private readonly UpdateImageCopyUseCase _updateUseCase;
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

    /// <summary>
    /// 「+ 新規」フライアウトを開いているか。<c>true</c> の間だけ View 側で名前入力 TextBox と
    /// 確定/キャンセルボタンが表示される。<see cref="GridCanvasListViewModel.IsCreating"/> と同パターン。
    /// </summary>
    [ObservableProperty]
    public partial bool IsCreating { get; set; }

    /// <summary>
    /// 新規作成フライアウトの名前ドラフト。空白だけ / 空文字なら従来通り「バリアント N」自動採番、
    /// 値があればそれを <see cref="CreateLogicalCopyUseCase"/> に渡す。
    /// </summary>
    [ObservableProperty]
    public partial string DraftCopyName { get; set; } = string.Empty;

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
        IImageStorage imageStorage,
        CreateLogicalCopyUseCase createUseCase,
        UpdateImageCopyUseCase updateUseCase,
        IMessenger messenger,
        IUndoRedoService history,
        ILogger<CopyListViewModel> logger)
    {
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _thumbnailService = thumbnailService;
        _imageStorage = imageStorage;
        _createUseCase = createUseCase;
        _updateUseCase = updateUseCase;
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
            // AutoCrop の画像クリックピッカー UI で使うパス・サイズを事前解決:
            //   サムネパス = 表示用、原画像パス + サイズ = 色採取用（サムネ圧縮で色が変化するため
            //   AutoCrop 走査と同じ原画像から取得する）
            var asset = await _assetRepository.FindByIdAsync(assetId.Value, ct);
            var thumbnailPath = asset is null ? null : _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var sourcePath = asset is null ? null : _imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            var sourceWidth = asset?.Size.Width ?? 0;
            var sourceHeight = asset?.Size.Height ?? 0;
            var copies = await _copyRepository.FindByAssetIdAsync(assetId.Value, ct);
            foreach (var copy in copies)
                Copies.Add(new CopyItemViewModel(copy, thumbnailPath, sourcePath, sourceWidth, sourceHeight));

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

    /// <summary>
    /// 新規作成フライアウトを開く。<see cref="DraftCopyName"/> を空にリセットして、
    /// View 側で名前入力 TextBox を表示する。アセット未選択 / IsBusy 中は no-op。
    /// </summary>
    [RelayCommand]
    public void BeginCreate()
    {
        if (_currentAssetId is null || IsBusy) return;
        DraftCopyName = string.Empty;
        IsCreating = true;
    }

    /// <summary>新規作成フライアウトを閉じる（作成しない）。</summary>
    [RelayCommand]
    public void CancelCreate()
    {
        IsCreating = false;
        DraftCopyName = string.Empty;
    }

    /// <summary>
    /// 新規作成フライアウトの確定。<see cref="DraftCopyName"/> を渡して
    /// <see cref="CreateCopyAsync"/> を呼び、成功/失敗にかかわらず最後に閉じる。
    /// 空白 / 空文字なら従来通り「バリアント N」自動採番。
    /// </summary>
    [RelayCommand]
    public async Task CommitCreateAsync(CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(DraftCopyName) ? null : DraftCopyName.Trim();
        await CreateCopyAsync(name, ct);
        IsCreating = false;
        DraftCopyName = string.Empty;
    }

    /// <summary>
    /// 新規論理コピーを作成して一覧に追加・選択する。
    /// <paramref name="customName"/> が <c>null</c> または空なら「バリアント N」（N = 既存件数+1）を自動採番。
    /// 値があれば trim して使う。新規作成は Undo 対象外（履歴は <see cref="IUndoRedoService.Clear"/> で消去）。
    /// テストやプログラム経由で直接呼べるよう RelayCommand から外し public method として残す。
    /// </summary>
    public async Task CreateCopyAsync(string? customName = null, CancellationToken ct = default)
    {
        if (_currentAssetId is null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            var ordinal = Copies.Count + 1;
            var nameToUse = string.IsNullOrWhiteSpace(customName)
                ? $"{Terminology.VariantPrefix} {ordinal}"
                : customName!.Trim();
            var result = await _createUseCase.ExecuteAsync(
                _currentAssetId.Value,
                copyName: nameToUse,
                ct: ct);

            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            var asset = await _assetRepository.FindByIdAsync(_currentAssetId.Value, ct);
            var thumbnailPath = asset is null ? null : _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var sourcePath = asset is null ? null : _imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            var sourceWidth = asset?.Size.Width ?? 0;
            var sourceHeight = asset?.Size.Height ?? 0;
            var item = new CopyItemViewModel(result.Value, thumbnailPath, sourcePath, sourceWidth, sourceHeight);
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

    /// <summary>
    /// インラインリネーム編集を開始する。<paramref name="item"/> の <see cref="CopyItemViewModel.IsEditing"/>=true、
    /// <see cref="CopyItemViewModel.EditingName"/> に現在の <see cref="CopyItemViewModel.CopyName"/> をコピー。
    /// 同時に他項目が編集中なら強制的にキャンセルする（同時編集を防ぐ）。
    /// </summary>
    public void BeginEdit(CopyItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        // 他項目が編集中なら閉じる（保存はしない、ユーザーが Enter したら確定する仕様）。
        foreach (var c in Copies)
        {
            if (!ReferenceEquals(c, item) && c.IsEditing)
            {
                c.IsEditing = false;
                c.EditingName = null;
            }
        }
        item.EditingName = item.CopyName;
        item.IsEditing = true;
    }

    /// <summary>
    /// インラインリネーム編集をキャンセル（保存しない）。<see cref="CopyItemViewModel.EditingName"/>
    /// は破棄、<see cref="CopyItemViewModel.CopyName"/> は元のまま。
    /// </summary>
    public void CancelEdit(CopyItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.IsEditing = false;
        item.EditingName = null;
        LogRenameCanceled(_logger, item.CopyId);
    }

    /// <summary>
    /// インラインリネームを確定して DB に保存する。<see cref="CopyItemViewModel.EditingName"/> を
    /// trim（空白だけなら null）した上で <see cref="CopyItemViewModel.CopyName"/> と比較し、
    /// 同じなら no-op、違えば <see cref="UpdateImageCopyCommand"/> を組み立てて
    /// <see cref="IUndoRedoService.ExecuteAsync"/> 経由で履歴に積む。Undo/Redo round-trip 対応。
    /// </summary>
    public async Task CommitEditAsync(CopyItemViewModel item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsEditing) return;

        var trimmed = string.IsNullOrWhiteSpace(item.EditingName) ? null : item.EditingName!.Trim();
        var beforeName = item.CopyName;
        // 編集状態は先に閉じる（保存中の View 再描画で TextBox にフォーカスが残らないように）。
        item.IsEditing = false;
        item.EditingName = null;

        if (string.Equals(trimmed, beforeName, StringComparison.Ordinal))
            return; // 変更なしなら履歴に積まない

        var before = new UpdateImageCopyChanges
        {
            CopyName = beforeName,
            ClearCopyName = beforeName is null,
        };
        var after = new UpdateImageCopyChanges
        {
            CopyName = trimmed,
            ClearCopyName = trimmed is null,
        };
        var beforeLabel = string.IsNullOrWhiteSpace(beforeName) ? Terminology.VariantUnnamed : beforeName!;
        var afterLabel = string.IsNullOrWhiteSpace(trimmed) ? Terminology.VariantUnnamed : trimmed!;
        var description = $"{Terminology.Variant}名変更: 「{beforeLabel}」→「{afterLabel}」";
        var command = new UpdateImageCopyCommand(_updateUseCase, item.CopyId, before, after, description);

        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return;
        }

        // 永続化が成功したので VM の表示も即時更新（DisplayName / SummaryLine が再計算される）。
        item.CopyName = trimmed;
        _messenger.Send(new CopyLibraryChangedMessage());
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "バリアント一覧を読み込み: asset={AssetId} count={Count}")]
    private static partial void LogLoaded(ILogger logger, Guid assetId, int count);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "バリアントのインラインリネームをキャンセル: {CopyId}")]
    private static partial void LogRenameCanceled(ILogger logger, Guid copyId);
}
