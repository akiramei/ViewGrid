using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブのワークスペース。アクティブグリッド、配置済み一覧、配置候補を保持し、
/// 配置/取消コマンドを提供する。
/// </summary>
public sealed partial class GridWorkspaceViewModel : ViewModelBase, IRecipient<CopyLibraryChangedMessage>
{
    private readonly IGridCanvasRepository _gridRepository;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly PlaceImageCopyUseCase _placeUseCase;
    private readonly RemovePlacementUseCase _removeUseCase;
    private readonly MovePlacementUseCase _moveUseCase;
    private readonly SwapPlacementsUseCase _swapUseCase;
    private readonly RenderGridUseCase _renderUseCase;
    private readonly ExportGridUseCase _exportUseCase;
    private readonly UpdateGridWeightsUseCase _updateWeightsUseCase;
    private readonly UpdatePlacementOffsetUseCase _updateOffsetUseCase;
    private readonly FitGridWeightToPlacementUseCase _fitWeightUseCase;
    private readonly IFilePickerService _filePicker;
    private readonly IMessenger _messenger;
    private readonly ILogger<GridWorkspaceViewModel> _logger;

    public PlacementInspectorViewModel Inspector { get; }

    [ObservableProperty]
    public partial GridCanvasItemViewModel? CurrentGrid { get; set; }

    [ObservableProperty]
    public partial CopyCandidateViewModel? SelectedCandidate { get; set; }

    [ObservableProperty]
    public partial PlacementItemViewModel? SelectedPlacement { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ObservableCollection<PlacementItemViewModel> Placements { get; } = [];
    public ObservableCollection<CopyCandidateViewModel> Candidates { get; } = [];

    public bool HasGrid => CurrentGrid is not null;

    public GridWorkspaceViewModel(
        IGridCanvasRepository gridRepository,
        IImageCopyRepository copyRepository,
        IImageAssetRepository assetRepository,
        IGridPlacementRepository placementRepository,
        IThumbnailService thumbnailService,
        PlaceImageCopyUseCase placeUseCase,
        RemovePlacementUseCase removeUseCase,
        MovePlacementUseCase moveUseCase,
        SwapPlacementsUseCase swapUseCase,
        RenderGridUseCase renderUseCase,
        ExportGridUseCase exportUseCase,
        UpdateGridWeightsUseCase updateWeightsUseCase,
        UpdatePlacementOffsetUseCase updateOffsetUseCase,
        FitGridWeightToPlacementUseCase fitWeightUseCase,
        IFilePickerService filePicker,
        IMessenger messenger,
        PlacementInspectorViewModel inspector,
        ILogger<GridWorkspaceViewModel> logger)
    {
        _gridRepository = gridRepository;
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _placementRepository = placementRepository;
        _thumbnailService = thumbnailService;
        _placeUseCase = placeUseCase;
        _removeUseCase = removeUseCase;
        _moveUseCase = moveUseCase;
        _swapUseCase = swapUseCase;
        _renderUseCase = renderUseCase;
        _exportUseCase = exportUseCase;
        _updateWeightsUseCase = updateWeightsUseCase;
        _updateOffsetUseCase = updateOffsetUseCase;
        _fitWeightUseCase = fitWeightUseCase;
        _filePicker = filePicker;
        _messenger = messenger;
        Inspector = inspector;
        _logger = logger;

        _messenger.Register(this);
    }

    /// <summary>SelectedPlacement 変更時に Inspector を Attach する。
    /// 描画域サイズの計算に CurrentGrid（重み・キャンバスサイズ）が必要なので渡す。</summary>
    partial void OnSelectedPlacementChanged(PlacementItemViewModel? value)
    {
        _ = Inspector.AttachAsync(value, CurrentGrid);
    }

    /// <summary>
    /// 候補ライブラリ変更通知を受信して候補を再ロードする。
    /// 失敗は握りつぶす（VM ライフサイクル中に常時購読しているため、
    /// 表示先タブが見えていない場合もある）。
    /// </summary>
    public void Receive(CopyLibraryChangedMessage message)
    {
        _ = ReloadFromMessageAsync();
    }

    /// <summary>テスト専用: Receive を await できる形で実行する。</summary>
    internal Task ReloadFromMessageAsyncForTests() => ReloadFromMessageAsync();

    private async Task ReloadFromMessageAsync()
    {
        try
        {
            await LoadCandidatesAsync();

            // 候補ライブラリ変更は cascade（Asset/Copy 削除 → 配置も削除）を伴うため、
            // DB 上の最新状態に追随するよう配置済み一覧も再ロードする。
            var grid = CurrentGrid;
            if (grid is null) return;

            var previousSelectedId = SelectedPlacement?.PlacementId;
            Placements.Clear();
            SelectedPlacement = null;
            await LoadPlacementsAsync(grid.GridId, default);

            // 再ロード前に選択していた配置がまだ存在するなら新インスタンスを選び直す
            // （Inspector の表示が消えないようにするため）
            if (previousSelectedId is not null)
                SelectedPlacement = Placements.FirstOrDefault(p => p.PlacementId == previousSelectedId.Value);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogReloadFailed(_logger, ex);
        }
    }

    /// <summary>
    /// 指定したグリッドをワークスペースに読み込む。null は何も表示しないクリア状態。
    /// </summary>
    public async Task LoadGridAsync(GridCanvasItemViewModel? grid, CancellationToken ct = default)
    {
        CurrentGrid = grid;
        OnPropertyChanged(nameof(HasGrid));

        Placements.Clear();
        SelectedPlacement = null;
        StatusMessage = null;

        if (grid is null)
            return;

        try
        {
            IsBusy = true;
            await LoadCandidatesAsync(ct);
            await LoadPlacementsAsync(grid.GridId, ct);
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; }
    }

    /// <summary>論理コピー候補リストを最新化する（タブ表示更新時にも呼ぶ）。</summary>
    public async Task LoadCandidatesAsync(CancellationToken ct = default)
    {
        var copies = await _copyRepository.FindAllAsync(ct);
        var assetIds = copies.Select(c => c.AssetId).Distinct().ToList();
        var assets = new Dictionary<Guid, ImageAsset>();
        foreach (var id in assetIds)
        {
            var a = await _assetRepository.FindByIdAsync(id, ct);
            if (a is not null) assets[id] = a;
        }

        Candidates.Clear();
        foreach (var copy in copies)
        {
            if (!assets.TryGetValue(copy.AssetId, out var asset))
                continue;
            var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            Candidates.Add(new CopyCandidateViewModel(copy, asset, thumb));
        }

        if (SelectedCandidate is not null && !Candidates.Any(c => c.CopyId == SelectedCandidate.CopyId))
            SelectedCandidate = null;
        SelectedCandidate ??= Candidates.FirstOrDefault();

        LogCandidatesLoaded(_logger, Candidates.Count);
    }

    private async Task LoadPlacementsAsync(Guid gridId, CancellationToken ct)
    {
        var placements = await _placementRepository.FindByGridIdAsync(gridId, ct);

        var copyCache = new Dictionary<Guid, ImageCopy>();
        var assetCache = new Dictionary<Guid, ImageAsset>();

        foreach (var p in placements)
        {
            if (!copyCache.TryGetValue(p.CopyId, out var copy))
            {
                copy = await _copyRepository.FindByIdAsync(p.CopyId, ct);
                if (copy is null) continue;
                copyCache[p.CopyId] = copy;
            }

            if (!assetCache.TryGetValue(copy.AssetId, out var asset))
            {
                asset = await _assetRepository.FindByIdAsync(copy.AssetId, ct);
                if (asset is null) continue;
                assetCache[copy.AssetId] = asset;
            }

            var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            Placements.Add(new PlacementItemViewModel(p, copy, asset, thumb));
        }

        LogPlacementsLoaded(_logger, gridId, Placements.Count);
    }

    /// <summary>
    /// 配置済みアイテムをドロップ位置へ移動、または既存配置と入れ替える。
    /// </summary>
    public async Task<bool> MoveOrSwapPlacementAsync(
        Guid sourcePlacementId,
        CellPosition dropPosition,
        CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return false;

        var source = Placements.FirstOrDefault(p => p.PlacementId == sourcePlacementId);
        if (source is null) return false;

        // ドロップ先セルにある別の配置を探す
        var target = Placements.FirstOrDefault(p =>
            p.PlacementId != sourcePlacementId &&
            dropPosition.X >= p.GridX && dropPosition.X < p.GridX + Math.Max(1, p.OccupyWidth) &&
            dropPosition.Y >= p.GridY && dropPosition.Y < p.GridY + Math.Max(1, p.OccupyHeight));

        try
        {
            IsBusy = true;
            if (target is null)
            {
                // Move
                var result = await _moveUseCase.ExecuteAsync(sourcePlacementId, dropPosition, ct);
                if (result.IsError)
                {
                    StatusMessage = string.Join(", ", result.Errors);
                    return false;
                }
                await ReloadPlacementsAsync(grid.GridId, ct);
                SelectedPlacement = Placements.FirstOrDefault(p => p.PlacementId == sourcePlacementId);
                StatusMessage = $"({dropPosition.X},{dropPosition.Y}) に移動しました。";
                return true;
            }
            else
            {
                // Swap
                var result = await _swapUseCase.ExecuteAsync(sourcePlacementId, target.PlacementId, ct);
                if (result.IsError)
                {
                    StatusMessage = string.Join(", ", result.Errors);
                    return false;
                }
                await ReloadPlacementsAsync(grid.GridId, ct);
                SelectedPlacement = Placements.FirstOrDefault(p => p.PlacementId == sourcePlacementId);
                StatusMessage = "配置を入れ替えました。";
                return true;
            }
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 指定した論理コピーを指定セルに配置する。D&D のドロップハンドラから呼ばれる。
    /// </summary>
    public async Task<bool> PlaceCopyAtAsync(Guid copyId, CellPosition position, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return false;

        try
        {
            IsBusy = true;
            var result = await _placeUseCase.ExecuteAsync(grid.GridId, copyId, position, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return false;
            }

            await ReloadPlacementsAsync(grid.GridId, ct);
            SelectedPlacement = Placements.FirstOrDefault(p => p.PlacementId == result.Value.Id);
            StatusMessage = $"({position.X},{position.Y}) に配置しました。";
            return true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task PlaceSelectedToFirstFreeCellAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        var candidate = SelectedCandidate;
        if (grid is null || candidate is null || IsBusy) return;

        try
        {
            IsBusy = true;
            var position = FindFirstFreeCell(grid, candidate.OccupySize);
            if (position is null)
            {
                StatusMessage = "空きセルが見つかりません。";
                return;
            }

            var result = await _placeUseCase.ExecuteAsync(grid.GridId, candidate.CopyId, position.Value, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            // 反映: VM 側のリストにアイテムを追加（再ロードで一括更新する）
            await ReloadPlacementsAsync(grid.GridId, ct);
            SelectedPlacement = Placements.FirstOrDefault(p => p.PlacementId == result.Value.Id);
            StatusMessage = $"({position.Value.X},{position.Value.Y}) に配置しました。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RemoveSelectedPlacementAsync(CancellationToken ct = default)
    {
        var target = SelectedPlacement;
        if (target is null || IsBusy) return;

        try
        {
            IsBusy = true;
            var result = await _removeUseCase.ExecuteAsync(target.PlacementId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            Placements.Remove(target);
            SelectedPlacement = Placements.FirstOrDefault();
            StatusMessage = "配置を削除しました。";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadPlacementsAsync(Guid gridId, CancellationToken ct)
    {
        Placements.Clear();
        await LoadPlacementsAsync(gridId, ct);
    }

    /// <summary>
    /// 左上から走査して、占有サイズが収まる最初の空きセルを返す。
    /// </summary>
    private CellPosition? FindFirstFreeCell(GridCanvasItemViewModel grid, OccupySize size)
    {
        var existing = Placements
            .Select(p => new ExistingPlacement(p.PlacementId, p.Position, p.OccupySize))
            .ToArray();

        for (var y = 0; y <= grid.Rows - size.Height; y++)
        {
            for (var x = 0; x <= grid.Cols - size.Width; x++)
            {
                var pos = new CellPosition(x, y);
                var validation = PlacementValidator.Validate(
                    size, pos, grid.Rows, grid.Cols, existing);
                if (validation.IsValid)
                    return pos;
            }
        }
        return null;
    }

    /// <summary>
    /// 現在のグリッドをレンダリングして PNG バイト列を返す（プレビュー用）。
    /// 失敗時は <c>null</c> を返し、<see cref="StatusMessage"/> にエラーを格納する。
    /// </summary>
    public async Task<byte[]?> RequestPreviewAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return null;

        try
        {
            IsBusy = true;
            var result = await _renderUseCase.ExecuteAsync(grid.GridId, ct);
            if (result.IsError)
            {
                StatusMessage = string.Join(", ", result.Errors);
                return null;
            }
            StatusMessage = $"プレビューを生成しました ({result.Value.Length:N0} bytes)";
            return result.Value;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// SaveDialog を出して指定パスへ PNG として書き出す。プレビュー経由しない高速パス。
    /// </summary>
    [RelayCommand]
    public async Task ExportToPngAsync(CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || IsBusy) return;

        var suggested = $"{SanitizeFileName(grid.Name)}.png";
        var path = await _filePicker.PickSavePngPathAsync(suggested, ct);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            IsBusy = true;
            var result = await _exportUseCase.ExecuteAsync(grid.GridId, path, ct);
            StatusMessage = result.IsError
                ? string.Join(", ", result.Errors)
                : $"出力しました: {Path.GetFileName(path)} ({result.Value.FileSizeBytes:N0} bytes)";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// プレビューで生成した既存 PNG バイト列を、SaveDialog で選んだパスに書き出す。
    /// </summary>
    public async Task<bool> SavePngBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null || bytes is null || bytes.Length == 0) return false;

        var suggested = $"{SanitizeFileName(grid.Name)}.png";
        var path = await _filePicker.PickSavePngPathAsync(suggested, ct);
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            StatusMessage = $"出力しました: {Path.GetFileName(path)} ({bytes.LongLength:N0} bytes)";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"保存に失敗しました: {ex.Message}";
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "viewgrid-export" : clean;
    }

    /// <summary>
    /// 境界ドラッグでの重み更新を保存する。<paramref name="colWeights"/> または
    /// <paramref name="rowWeights"/> のどちらかが null（変更なし）でも構わない。
    /// 成功時は <see cref="CurrentGrid"/> の重みを更新して View にリビルドさせる。
    /// </summary>
    public async Task<bool> ApplyGridWeightsAsync(
        IReadOnlyList<int>? colWeights,
        IReadOnlyList<int>? rowWeights,
        CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;

        var result = await _updateWeightsUseCase.ExecuteAsync(grid.GridId, colWeights, rowWeights, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // VM 側の重みを更新する。GridCanvasView は CurrentGrid の PropertyChanged を見て Rebuild
        // するので、明示的に CurrentGrid 通知を発火（参照は同じだが内容が変わったため）。
        grid.ColWeights = result.Value.ColWeights;
        grid.RowWeights = result.Value.RowWeights;
        OnPropertyChanged(nameof(CurrentGrid));
        StatusMessage = "グリッド比率を更新しました。";
        return true;
    }

    /// <summary>
    /// Shift+ドラッグ等のキャンバス操作で配置の <see cref="GridPlacement.PixelOffsetX"/> /
    /// <c>PixelOffsetY</c> を永続化する。値は <see cref="PlacementInspectorViewModel.MaxPixelOffset"/>
    /// で clamp される。成功時は VM 内の対応 <see cref="PlacementItemViewModel"/> も同期更新するため、
    /// 再ロードは不要。
    /// </summary>
    public async Task<bool> ApplyPixelOffsetAsync(
        Guid placementId, int pixelOffsetX, int pixelOffsetY, CancellationToken ct = default)
    {
        var max = PlacementInspectorViewModel.MaxPixelOffset;
        var clampedX = Math.Clamp(pixelOffsetX, -max, max);
        var clampedY = Math.Clamp(pixelOffsetY, -max, max);

        var result = await _updateOffsetUseCase.ExecuteAsync(placementId, clampedX, clampedY, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        var item = Placements.FirstOrDefault(p => p.PlacementId == placementId);
        if (item is not null)
        {
            item.PixelOffsetX = clampedX;
            item.PixelOffsetY = clampedY;
        }
        StatusMessage = $"ピクセル微調整を保存しました (ΔX={clampedX}, ΔY={clampedY})。";
        return true;
    }

    /// <summary>
    /// 指定 placement の実描画矩形に合わせて、占有列幅または行高を縮める。
    /// 余白は隣接列/行に分配（端列/端行で隣接がない側の余白は破棄）。
    /// 成功時は最新の重みを <see cref="CurrentGrid"/> に反映し、View を再構築させる。
    /// </summary>
    public async Task<bool> FitGridWeightAsync(
        Guid placementId, FitAxis axis, CancellationToken ct = default)
    {
        var grid = CurrentGrid;
        if (grid is null) return false;

        var oldCol = grid.ColWeights;
        var oldRow = grid.RowWeights;

        var result = await _fitWeightUseCase.ExecuteAsync(placementId, axis, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }

        // 重みが変わった可能性があるので、グリッドを再読込して反映
        var reloaded = await _gridRepository.FindByIdAsync(grid.GridId, ct);
        if (reloaded is null) return false;

        var changed =
            !reloaded.ColWeights.SequenceEqual(oldCol) ||
            !reloaded.RowWeights.SequenceEqual(oldRow);

        grid.ColWeights = reloaded.ColWeights;
        grid.RowWeights = reloaded.RowWeights;
        OnPropertyChanged(nameof(CurrentGrid));

        StatusMessage = changed
            ? (axis == FitAxis.Column ? "列幅を画像にフィットしました。" : "行高を画像にフィットしました。")
            : "フィット対象なし（余白がない、または計算範囲外）。";
        return true;
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "配置候補を読み込み: {Count} 件")]
    private static partial void LogCandidatesLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "配置を読み込み: grid={GridId} count={Count}")]
    private static partial void LogPlacementsLoaded(ILogger logger, Guid gridId, int count);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Warning, Message = "候補・配置の自動更新に失敗")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex);
}
