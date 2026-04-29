using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブで選択された <see cref="PlacementItemViewModel"/> の<b>配置固有プロパティ</b>を編集する。
/// <para>
/// 配置固有プロパティは現状 <see cref="PlacementItemViewModel.PixelOffsetX"/> /
/// <see cref="PlacementItemViewModel.PixelOffsetY"/> のみ。共有特性（Rotation/Flip/Scaling/
/// Trim/Align/Occupy）は <see cref="ImageCopy"/> 単位で持つため、
/// 「特性を編集 →」コマンドで <see cref="NavigateToCopyPropertiesMessage"/> を送出し、
/// 準備タブの <c>CopyPropertiesView</c> に編集を委譲する。
/// </para>
/// <para>
/// この設計により、配置タブの Inspector と準備タブの CopyProperties で共有特性を
/// 二重に編集できる従来構造（共有バナーが必要だった原因）を解消し、
/// Inspector を「配置固有の微調整」だけの最小構成に絞れる。
/// </para>
/// </summary>
public sealed partial class PlacementInspectorViewModel : ObservableObject
{
    private readonly UpdatePlacementOffsetUseCase _offsetUseCase;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly IUndoRedoService _history;
    private readonly IMessenger _messenger;
    private readonly ILogger<PlacementInspectorViewModel> _logger;

    /// <summary>1 軸あたりの ΔX/ΔY の上限（VM 側でサニティ丸め）。</summary>
    public const int MaxPixelOffset = 4096;

    private PlacementItemViewModel? _source;
    private GridCanvasItemViewModel? _grid;
    private bool _suppressDirty;

    [ObservableProperty]
    public partial bool HasPlacement { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string HeaderLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PositionLabel { get; set; } = string.Empty;

    /// <summary>
    /// 選択中の配置がキャンバス上で占める描画域のピクセル寸法を示すラベル
    /// （例: "画像描画域: 640×480 px"）。Shift+ドラッグでの微調整時に
    /// 数値の感覚を掴むための参考情報として表示する。
    /// </summary>
    [ObservableProperty]
    public partial string ImageDrawSizeLabel { get; set; } = string.Empty;

    // 編集バッファ（配置固有のみ）
    [ObservableProperty] public partial int PixelOffsetX { get; set; }
    [ObservableProperty] public partial int PixelOffsetY { get; set; }

    public PlacementInspectorViewModel(
        UpdatePlacementOffsetUseCase offsetUseCase,
        IGridPlacementRepository placementRepository,
        IUndoRedoService history,
        IMessenger messenger,
        ILogger<PlacementInspectorViewModel> logger)
    {
        _offsetUseCase = offsetUseCase;
        _placementRepository = placementRepository;
        _history = history;
        _messenger = messenger;
        _logger = logger;
        PropertyChanged += OnAnyPropertyChanged;
    }

    /// <summary>編集対象の placement を差し替える。null で無効状態。
    /// <paramref name="grid"/> を渡すと描画域サイズ（<see cref="ImageDrawSizeLabel"/>）
    /// を計算する（null なら空ラベル）。新しい source の <c>PropertyChanged</c> を購読し、
    /// 外部（Shift+ドラッグ等）から <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> が変更されたら Inspector の表示にも追従させる。</summary>
    public Task AttachAsync(
        PlacementItemViewModel? source,
        GridCanvasItemViewModel? grid = null,
        CancellationToken ct = default)
    {
        if (_source is not null)
            _source.PropertyChanged -= OnSourcePropertyChanged;

        _source = source;
        _grid = grid;

        if (_source is not null)
            _source.PropertyChanged += OnSourcePropertyChanged;

        _suppressDirty = true;
        try
        {
            if (source is null)
            {
                HasPlacement = false;
                HeaderLabel = string.Empty;
                PositionLabel = string.Empty;
                ImageDrawSizeLabel = string.Empty;
                PixelOffsetX = 0;
                PixelOffsetY = 0;
            }
            else
            {
                HasPlacement = true;
                HeaderLabel = source.Label;
                PositionLabel = $"位置: ({source.GridX},{source.GridY}) / 占有: {source.OccupyWidth}×{source.OccupyHeight}";
                ImageDrawSizeLabel = ComputeImageDrawSizeLabel(source, grid);
                PixelOffsetX = source.PixelOffsetX;
                PixelOffsetY = source.PixelOffsetY;
            }
            IsDirty = false;
            StatusMessage = null;
        }
        finally
        {
            _suppressDirty = false;
        }

        EditCopyPropertiesCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 選択中の配置がキャンバス上で占める描画域（セル矩形、PixelOffset=0、ScalingMode 不問）の
    /// ピクセル寸法ラベルを生成する。<paramref name="grid"/> が <c>null</c>、または計算に必要な
    /// 寸法情報が欠けている場合は空文字列を返す。
    /// </summary>
    private static string ComputeImageDrawSizeLabel(PlacementItemViewModel source, GridCanvasItemViewModel? grid)
    {
        if (grid is null) return string.Empty;
        if (grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0 || grid.Cols <= 0 || grid.Rows <= 0)
            return string.Empty;

        var canvas = new PixelSize(grid.CanvasWidth, grid.CanvasHeight);
        var rect = PlacementGeometry.ComputeDestRect(
            canvas, grid.Cols, grid.Rows,
            grid.ColWeights, grid.RowWeights,
            new CellPosition(source.GridX, source.GridY),
            new OccupySize(Math.Max(1, source.OccupyWidth), Math.Max(1, source.OccupyHeight)));
        return $"画像描画域: {rect.Width}×{rect.Height} px";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        // _source は外部要因で null 化される可能性があるためローカルキャプチャ。
        var source = _source;
        var grid = _grid;
        if (source is null || grid is null || !IsDirty) return;

        var clampedX = Math.Clamp(PixelOffsetX, -MaxPixelOffset, MaxPixelOffset);
        var clampedY = Math.Clamp(PixelOffsetY, -MaxPixelOffset, MaxPixelOffset);

        // before の値は DB の永続化済み値から取得（Shift+ドラッグで _source が書き換わっている可能性がある）
        var current = await _placementRepository.FindByIdAsync(source.PlacementId, ct);
        if (current is null)
        {
            StatusMessage = $"GridPlacement {source.PlacementId} が見つかりません。";
            return;
        }
        if (current.PixelOffsetX == clampedX && current.PixelOffsetY == clampedY)
        {
            // 値変化なし — 履歴に積まない
            _suppressDirty = true;
            try { IsDirty = false; StatusMessage = "保存しました（変更なし）。"; }
            finally { _suppressDirty = false; }
            return;
        }

        var description = $"ピクセル微調整: 「{source.Label}」 ΔX={clampedX}, ΔY={clampedY}";
        var command = new UpdatePlacementOffsetCommand(
            _offsetUseCase, grid.GridId, source.PlacementId,
            current.PixelOffsetX, current.PixelOffsetY, clampedX, clampedY, description);
        var execResult = await _history.ExecuteAsync(command, ct);
        if (execResult.IsError)
        {
            StatusMessage = string.Join(", ", execResult.Errors);
            return;
        }

        // VM 側の表示用 PlacementItemViewModel にも同期。Shift+ドラッグ系で
        // 既に同値ならイベント連鎖はないため、再入の懸念はない。
        source.PixelOffsetX = clampedX;
        source.PixelOffsetY = clampedY;

        _suppressDirty = true;
        try
        {
            IsDirty = false;
            StatusMessage = "保存しました。";
        }
        finally
        {
            _suppressDirty = false;
        }

        LogSaved(_logger, source.PlacementId);
    }

    [RelayCommand(CanExecute = nameof(CanRevert))]
    public Task RevertAsync(CancellationToken ct = default) => AttachAsync(_source, _grid, ct);

    private bool CanSave() => HasPlacement && IsDirty;
    private bool CanRevert() => HasPlacement && IsDirty;

    /// <summary>
    /// ΔX/ΔY を 0 に戻す。「0 にリセット」ボタン用。値を 0 に戻すだけの操作なので
    /// 常に有効（配置が選択されていない場合は <see cref="_source"/> が null で
    /// 反映されないだけで害はない）。
    /// </summary>
    [RelayCommand]
    public void ResetPixelOffset()
    {
        PixelOffsetX = 0;
        PixelOffsetY = 0;
    }

    /// <summary>
    /// 「特性を編集 →」コマンド。準備タブに切り替えて当該論理コピーを編集する画面に
    /// 飛ばすため、<see cref="NavigateToCopyPropertiesMessage"/> を送る。
    /// 受信側は <c>MainWindowViewModel</c>。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditCopyProperties))]
    public void EditCopyProperties()
    {
        var source = _source;
        if (source is null) return;
        _messenger.Send(new NavigateToCopyPropertiesMessage(source.AssetId, source.CopyId));
    }

    private bool CanEditCopyProperties() => HasPlacement;

    /// <summary>
    /// 外部（Shift+ドラッグ等）からの <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> 変更を Inspector の表示にも反映させる。Shift+ドラッグは「Inspector 編集の
    /// 代替手段」なので、IsDirty を立てて保存ボタン経由で永続化する設計に統一する
    /// （以前は自動保存していたが、編集と保存の責務分離が崩れる UX 上の違和感があったため改修）。
    /// </summary>
    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PlacementItemViewModel src || src != _source) return;

        if (e.PropertyName is nameof(PlacementItemViewModel.PixelOffsetX))
        {
            PixelOffsetX = src.PixelOffsetX;
        }
        else if (e.PropertyName is nameof(PlacementItemViewModel.PixelOffsetY))
        {
            PixelOffsetY = src.PixelOffsetY;
        }
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirty) return;
        if (e.PropertyName is nameof(IsDirty) or nameof(HasPlacement)
            or nameof(StatusMessage) or nameof(HeaderLabel) or nameof(PositionLabel)
            or nameof(ImageDrawSizeLabel))
            return;

        if (!IsDirty) IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "配置インスペクタからピクセル微調整を保存: {PlacementId}")]
    private static partial void LogSaved(ILogger logger, Guid placementId);
}
