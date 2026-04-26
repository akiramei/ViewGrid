using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 選択中アセットに紐づく論理コピー一覧を管理する。
/// アセット選択の変更は外部（MainWindowViewModel）から LoadForAssetAsync 経由で通知する。
/// </summary>
public sealed partial class CopyListViewModel : ViewModelBase
{
    private readonly IImageCopyRepository _copyRepository;
    private readonly CreateLogicalCopyUseCase _createUseCase;
    private readonly IMessenger _messenger;
    private readonly ILogger<CopyListViewModel> _logger;

    private Guid? _currentAssetId;

    [ObservableProperty]
    public partial CopyItemViewModel? SelectedCopy { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ObservableCollection<CopyItemViewModel> Copies { get; } = [];

    public bool HasAsset => _currentAssetId.HasValue;

    public CopyListViewModel(
        IImageCopyRepository copyRepository,
        CreateLogicalCopyUseCase createUseCase,
        IMessenger messenger,
        ILogger<CopyListViewModel> logger)
    {
        _copyRepository = copyRepository;
        _createUseCase = createUseCase;
        _messenger = messenger;
        _logger = logger;
    }

    public async Task LoadForAssetAsync(Guid? assetId, CancellationToken ct = default)
    {
        _currentAssetId = assetId;
        OnPropertyChanged(nameof(HasAsset));

        Copies.Clear();
        SelectedCopy = null;
        StatusMessage = null;

        if (assetId is null)
            return;

        try
        {
            IsBusy = true;
            var copies = await _copyRepository.FindByAssetIdAsync(assetId.Value, ct);
            foreach (var copy in copies)
                Copies.Add(new CopyItemViewModel(copy));

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

            var item = new CopyItemViewModel(result.Value);
            Copies.Add(item);
            SelectedCopy = item;
            StatusMessage = $"「{item.DisplayName}」を作成しました。";
            _messenger.Send(new CopyLibraryChangedMessage());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedCopyAsync(CancellationToken ct = default)
    {
        var selected = SelectedCopy;
        if (selected is null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            var result = await _copyRepository.DeleteAsync(selected.CopyId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            Copies.Remove(selected);
            SelectedCopy = Copies.FirstOrDefault();
            StatusMessage = $"「{selected.DisplayName}」を削除しました。";
            _messenger.Send(new CopyLibraryChangedMessage());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "論理コピー一覧を読み込み: asset={AssetId} count={Count}")]
    private static partial void LogLoaded(ILogger logger, Guid assetId, int count);
}
