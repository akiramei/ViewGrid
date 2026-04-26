using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AssetLibraryViewModel> _logger;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial AssetItemViewModel? SelectedAsset { get; set; }

    public ObservableCollection<AssetItemViewModel> Assets { get; } = [];

    public AssetLibraryViewModel(
        ImportImageUseCase importUseCase,
        DeleteImageAssetUseCase deleteUseCase,
        IImageAssetRepository assetRepository,
        IThumbnailService thumbnailService,
        IFilePickerService filePickerService,
        IMessenger messenger,
        ILogger<AssetLibraryViewModel> logger)
    {
        _importUseCase = importUseCase;
        _deleteUseCase = deleteUseCase;
        _assetRepository = assetRepository;
        _thumbnailService = thumbnailService;
        _filePickerService = filePickerService;
        _messenger = messenger;
        _logger = logger;
    }

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
    /// D&D 経由で呼ぶためのエントリポイント。複数ファイルを順に取り込み、
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
                _messenger.Send(new CopyLibraryChangedMessage());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        var selected = SelectedAsset;
        if (selected is null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            var result = await _deleteUseCase.ExecuteAsync(selected.AssetId, ct);
            if (result.IsError)
            {
                StatusMessage = $"削除に失敗しました: {string.Join(", ", result.Errors)}";
                return;
            }

            Assets.Remove(selected);
            SelectedAsset = null;
            StatusMessage = $"{selected.DisplayName} を削除しました。";

            // Asset 削除は cascade で関連 ImageCopy も削除されるため候補にも反映。
            _messenger.Send(new CopyLibraryChangedMessage());
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
        foreach (var asset in assets)
            Assets.Add(BuildItem(asset));
    }

    private AssetItemViewModel BuildItem(ImageAsset asset)
    {
        var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
        return new AssetItemViewModel(asset, thumb);
    }

    private static string BuildStatus(int imported, int duplicated, int failed)
    {
        var parts = new List<string>();
        if (imported > 0) parts.Add($"{imported} 件追加");
        if (duplicated > 0) parts.Add($"{duplicated} 件は既存（重複）");
        if (failed > 0) parts.Add($"{failed} 件失敗");
        return parts.Count > 0 ? string.Join(" / ", parts) : "変更なし";
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "アセット一覧を読み込み: {Count} 件")]
    private static partial void LogLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "画像取り込み失敗 path={Path}: {Errors}")]
    private static partial void LogImportFailed(ILogger logger, string path, string errors);
}
