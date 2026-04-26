using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブで選択された <see cref="PlacementItemViewModel"/> の特性を編集する。
/// 共有特性（Rotation/Flip/Scaling/Trim/Align/Occupy）は <see cref="UpdateImageCopyUseCase"/> 経由で
/// <see cref="ImageCopy"/> を更新する。同じ論理コピーを複数セルに配置している場合は変更が全配置に波及するため、
/// <see cref="SharedPlacementCount"/> と <see cref="HasSharedPlacements"/> でその旨を UI に伝える。
/// </summary>
public sealed partial class PlacementInspectorViewModel : ObservableObject
{
    private readonly UpdateImageCopyUseCase _updateUseCase;
    private readonly UpdatePlacementOffsetUseCase _offsetUseCase;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IGridPlacementRepository _placementRepository;
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

    /// <summary>同じ論理コピーを参照している配置の総数（自分自身含む）。</summary>
    [ObservableProperty]
    public partial int SharedPlacementCount { get; set; }

    public bool HasSharedPlacements => SharedPlacementCount > 1;

    // 編集バッファ
    [ObservableProperty] public partial Rotation Rotation { get; set; }
    [ObservableProperty] public partial bool FlipX { get; set; }
    [ObservableProperty] public partial bool FlipY { get; set; }
    [ObservableProperty] public partial ScalingMode ScalingMode { get; set; } = ScalingMode.UniformContain;
    [ObservableProperty] public partial AnchorX TrimAnchorX { get; set; } = AnchorX.Center;
    [ObservableProperty] public partial AnchorY TrimAnchorY { get; set; } = AnchorY.Center;
    [ObservableProperty] public partial AnchorX AlignX { get; set; } = AnchorX.Center;
    [ObservableProperty] public partial AnchorY AlignY { get; set; } = AnchorY.Center;
    [ObservableProperty] public partial int OccupyWidth { get; set; } = 1;
    [ObservableProperty] public partial int OccupyHeight { get; set; } = 1;
    [ObservableProperty] public partial int PixelOffsetX { get; set; }
    [ObservableProperty] public partial int PixelOffsetY { get; set; }

    // XAML バインディング用
    public IReadOnlyList<Rotation> RotationOptions { get; } =
        [Rotation.None, Rotation.Cw90, Rotation.Cw180, Rotation.Cw270];

    public IReadOnlyList<AnchorX> AnchorXOptions { get; } =
        [AnchorX.Left, AnchorX.Center, AnchorX.Right];

    public IReadOnlyList<AnchorY> AnchorYOptions { get; } =
        [AnchorY.Top, AnchorY.Center, AnchorY.Bottom];

    public IReadOnlyList<ScalingMode> ScalingModeOptions { get; } =
    [
        ScalingMode.None,
        ScalingMode.UniformContain,
        ScalingMode.UniformContainShrinkOnly,
        ScalingMode.UniformContainEnlargeOnly,
        ScalingMode.UniformCover,
        ScalingMode.Fill,
    ];

    public PlacementInspectorViewModel(
        UpdateImageCopyUseCase updateUseCase,
        UpdatePlacementOffsetUseCase offsetUseCase,
        IImageCopyRepository copyRepository,
        IGridPlacementRepository placementRepository,
        IMessenger messenger,
        ILogger<PlacementInspectorViewModel> logger)
    {
        _updateUseCase = updateUseCase;
        _offsetUseCase = offsetUseCase;
        _copyRepository = copyRepository;
        _placementRepository = placementRepository;
        _messenger = messenger;
        _logger = logger;
        PropertyChanged += OnAnyPropertyChanged;
    }

    /// <summary>編集対象の placement を差し替える。null で無効状態。
    /// <paramref name="grid"/> を渡すと描画域サイズ（<see cref="ImageDrawSizeLabel"/>）
    /// を計算する（null なら空ラベル）。新しい source の <c>PropertyChanged</c> を購読し、
    /// 外部（Shift+ドラッグ等）から <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> が変更されたら Inspector の表示にも追従させる。</summary>
    public async Task AttachAsync(
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
                SharedPlacementCount = 0;
                ResetBuffer();
            }
            else
            {
                HasPlacement = true;
                HeaderLabel = source.Label;
                PositionLabel = $"位置: ({source.GridX},{source.GridY}) / 占有: {source.OccupyWidth}×{source.OccupyHeight}";
                ImageDrawSizeLabel = ComputeImageDrawSizeLabel(source, grid);
                Rotation = source.Rotation;
                FlipX = source.FlipX;
                FlipY = source.FlipY;
                ScalingMode = source.ScalingMode;
                TrimAnchorX = source.TrimmingAnchor.X;
                TrimAnchorY = source.TrimmingAnchor.Y;
                AlignX = source.Alignment.X;
                AlignY = source.Alignment.Y;
                OccupyWidth = source.OccupySize.Width;
                OccupyHeight = source.OccupySize.Height;
                PixelOffsetX = source.PixelOffsetX;
                PixelOffsetY = source.PixelOffsetY;

                // 同じ論理コピーを参照する配置数を計算
                var siblings = await _placementRepository.FindByGridIdAsync(source.GridId, ct);
                SharedPlacementCount = siblings.Count(p => p.CopyId == source.CopyId);
                OnPropertyChanged(nameof(HasSharedPlacements));
            }
            IsDirty = false;
            StatusMessage = null;
        }
        finally
        {
            _suppressDirty = false;
        }
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
        // _source は messenger 受信に伴う再ロードで null 化される可能性があるため、
        // メソッド冒頭でローカルにキャプチャしてレース条件を回避する。
        var source = _source;
        if (source is null || !IsDirty) return;

        var current = await _copyRepository.FindByIdAsync(source.CopyId, ct);
        if (current is null)
        {
            StatusMessage = "対象の論理コピーが見つかりません。";
            return;
        }

        var changes = new UpdateImageCopyChanges
        {
            Transform = new ImageTransform(Rotation, FlipX, FlipY),
            ScalingMode = ScalingMode,
            TrimmingAnchor = new TrimmingAnchor(TrimAnchorX, TrimAnchorY),
            Alignment = new Alignment(AlignX, AlignY),
            OccupySize = new OccupySize(Math.Max(1, OccupyWidth), Math.Max(1, OccupyHeight)),
        };

        var result = await _updateUseCase.ExecuteAsync(source.CopyId, changes, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return;
        }

        // 配置別の PixelOffset は ImageCopy ではなく GridPlacement に持つので別 use case で保存。
        var clampedX = Math.Clamp(PixelOffsetX, -MaxPixelOffset, MaxPixelOffset);
        var clampedY = Math.Clamp(PixelOffsetY, -MaxPixelOffset, MaxPixelOffset);
        var offsetResult = await _offsetUseCase.ExecuteAsync(source.PlacementId, clampedX, clampedY, ct);
        if (offsetResult.IsError)
        {
            StatusMessage = string.Join(", ", offsetResult.Errors);
            return;
        }

        var sharedCount = SharedPlacementCount;
        _suppressDirty = true;
        try
        {
            IsDirty = false;
            StatusMessage = sharedCount > 1
                ? $"保存しました。{sharedCount} 件の配置に反映されます。"
                : "保存しました。";
        }
        finally
        {
            _suppressDirty = false;
        }

        LogSaved(_logger, source.CopyId);
        // メッセージ送信は最後（受信側の再ロードで _source が null 化される可能性があるため）
        _messenger.Send(new CopyLibraryChangedMessage());
    }

    [RelayCommand(CanExecute = nameof(CanRevert))]
    public Task RevertAsync(CancellationToken ct = default) => AttachAsync(_source, _grid, ct);

    private bool CanSave() => HasPlacement && IsDirty;
    private bool CanRevert() => HasPlacement && IsDirty;

    private void ResetBuffer()
    {
        Rotation = Rotation.None;
        FlipX = false;
        FlipY = false;
        ScalingMode = ScalingMode.UniformContain;
        TrimAnchorX = AnchorX.Center;
        TrimAnchorY = AnchorY.Center;
        AlignX = AnchorX.Center;
        AlignY = AnchorY.Center;
        OccupyWidth = 1;
        OccupyHeight = 1;
        PixelOffsetX = 0;
        PixelOffsetY = 0;
    }

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
            or nameof(ImageDrawSizeLabel)
            or nameof(SharedPlacementCount) or nameof(HasSharedPlacements))
            return;

        if (!IsDirty) IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "配置インスペクタから論理コピー特性を保存: {CopyId}")]
    private static partial void LogSaved(ILogger logger, Guid copyId);
}
