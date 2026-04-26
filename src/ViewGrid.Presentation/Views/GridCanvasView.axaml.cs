using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;

namespace ViewGrid.Presentation.Views;

public partial class GridCanvasView : UserControl
{
    private const double DragThreshold = 2.0;
    private const string CopyPrefix = "copy:";
    private const string PlacementPrefix = "placement:";

    private GridWorkspaceViewModel? _vm;

    private Point? _placementPressOrigin;
    private PlacementItemViewModel? _placementPressItem;
    private PointerPressedEventArgs? _placementPressEvent;
    private Border? _placementPressBorder;

    // Shift+ドラッグでの PixelOffset 微調整モード状態
    private bool _pixelOffsetDragging;
    private Point _pixelOffsetStart;
    private int _pixelOffsetStartX;
    private int _pixelOffsetStartY;
    private PlacementItemViewModel? _pixelOffsetTarget;
    private Border? _pixelOffsetBorder;

    // セル位置 → セル Border 参照（範囲ハイライトの一括クリアに使う）
    private readonly Dictionary<CellPosition, Border> _cellBorders = new();

    // 配置済み Border の元の枠スタイル（DragOver で変更したものを DragLeave で復元するため）
    private readonly Dictionary<Border, (IBrush Brush, Thickness Thickness, IBrush? Background)> _placementVisualOriginals = new();

    // 配置済み Border → 対応する placement VM。SizeChanged 時に PixelOffset の換算を再適用する。
    private readonly Dictionary<Border, PlacementItemViewModel> _placementBorders = new();

    public GridCanvasView()
    {
        InitializeComponent();
        this.GetObservable(DataContextProperty).Subscribe(new AnonymousObserver<object?>(OnDataContextChanged));
        CanvasGrid.SizeChanged += OnCanvasGridSizeChanged;
        // ドラッグ中の PointerMoved/Released は BoundaryOverlay 全体で受ける。
        // Capture は handle ではなく BoundaryOverlay 側に張り直す（OnBoundaryPointerPressed 内）。
        BoundaryOverlay.PointerMoved += OnOverlayPointerMoved;
        BoundaryOverlay.PointerReleased += OnOverlayPointerReleased;
        BoundaryOverlay.PointerCaptureLost += OnOverlayPointerCaptureLost;
    }

    private void OnCanvasGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 表示サイズが変わったら全配置の PixelOffset 換算を再計算する。
        foreach (var (border, placement) in _placementBorders)
            ApplyPixelOffsetTransform(border, placement);
    }

    private void OnDataContextChanged(object? newDataContext)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Placements.CollectionChanged -= OnPlacementsChanged;
        }

        _vm = newDataContext as GridWorkspaceViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Placements.CollectionChanged += OnPlacementsChanged;
        }

        Rebuild();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GridWorkspaceViewModel.CurrentGrid)
            or nameof(GridWorkspaceViewModel.SelectedPlacement))
        {
            Rebuild();
        }
    }

    private void OnPlacementsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        CanvasGrid.Children.Clear();
        CanvasGrid.RowDefinitions.Clear();
        CanvasGrid.ColumnDefinitions.Clear();
        _placementVisualOriginals.Clear();
        _cellBorders.Clear();
        _placementBorders.Clear();

        var grid = _vm?.CurrentGrid;
        if (grid is null)
            return;

        // 重み配列があれば各行・列に Star 重みを反映、無ければ均等。
        for (var r = 0; r < grid.Rows; r++)
        {
            var weight = r < grid.RowWeights.Length ? Math.Max(1, grid.RowWeights[r]) : 1;
            CanvasGrid.RowDefinitions.Add(new RowDefinition(new GridLength(weight, GridUnitType.Star)));
        }
        for (var c = 0; c < grid.Cols; c++)
        {
            var weight = c < grid.ColWeights.Length ? Math.Max(1, grid.ColWeights[c]) : 1;
            CanvasGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star)));
        }

        // Layer 1: セル枠（D&D ドロップターゲット）
        for (var r = 0; r < grid.Rows; r++)
        {
            for (var c = 0; c < grid.Cols; c++)
            {
                var pos = new CellPosition(c, r);
                var cell = new Border
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5),
                    Background = Brushes.Transparent,
                    Tag = pos,
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                DragDrop.SetAllowDrop(cell, true);
                cell.AddHandler(DragDrop.DragOverEvent, OnCellDragOver);
                cell.AddHandler(DragDrop.DragLeaveEvent, OnCellDragLeave);
                cell.AddHandler(DragDrop.DropEvent, OnCellDrop);
                CanvasGrid.Children.Add(cell);
                _cellBorders[pos] = cell;
            }
        }

        if (_vm is null)
            return;

        // Layer 2: 配置済み（自身もドロップ対象 = 入れ替え）
        foreach (var placement in _vm.Placements)
        {
            var visual = BuildPlacementVisual(placement, _vm.SelectedPlacement?.PlacementId == placement.PlacementId);
            Grid.SetRow(visual, placement.GridY);
            Grid.SetColumn(visual, placement.GridX);
            Grid.SetRowSpan(visual, Math.Max(1, placement.OccupyHeight));
            Grid.SetColumnSpan(visual, Math.Max(1, placement.OccupyWidth));
            CanvasGrid.Children.Add(visual);

            _placementBorders[visual] = placement;
            ApplyPixelOffsetTransform(visual, placement);
        }

        // Layer 3: 境界ドラッグハンドル（A2: 列・行比率の動的調整）
        BuildBoundaryHandles(grid);

        // 環境差（PowerShell 親プロセスから起動した場合等）で RowDefinitions/ColumnDefinitions
        // の Clear/Add が自動レイアウト更新を発火しないケースの保険として、明示的に
        // InvalidateMeasure を呼んで Star Sizing の再計算を強制する。
        CanvasGrid.InvalidateMeasure();
    }

    // ---------- A2: 境界ドラッグで列・行重みを動的調整 ----------

    private const double CanvasFixedSize = 600.0; // axaml の Width/Height 600 に合わせる
    private const double HandleHitWidth = 12.0;   // ドラッグハンドルの掴み幅（px）。視認性も兼ねて広めに。

    private GridCanvasItemViewModel? _draggingGrid;
    private bool _draggingIsCol;
    private int _draggingBoundaryIndex; // i (0-based, between cell i-1 and cell i)
    private double _dragStartPos;        // Canvas 座標での押下位置 (col=x, row=y)
    private ImmutableArray<int> _dragStartWeights;
    private Rectangle? _draggingHandle;

    private void BuildBoundaryHandles(GridCanvasItemViewModel grid)
    {
        // Rebuild が走るタイミングではドラッグ状態を保険でリセット
        _draggingGrid = null;
        _draggingHandle = null;
        BoundaryOverlay.Background = null;
        BoundaryOverlay.Children.Clear();

        // ハンドルは CanvasGrid 内に Grid.SetColumn/Row で配置する。
        // これにより Avalonia の Grid Layout が決定する実セル境界と必ず一致する
        // （BoundaryOverlay の Canvas 絶対座標とは独立に、Grid のレイアウト計算を信頼する）。
        var idleFill = new SolidColorBrush(Color.FromArgb(0x55, 0x33, 0x99, 0xFF));
        var hoverFill = new SolidColorBrush(Color.FromArgb(0xAA, 0x33, 0x99, 0xFF));

        // 列境界ハンドル: 列 i-1 と列 i の境界 = col=i セルの左端中心に配置
        for (var i = 1; i < grid.Cols; i++)
        {
            var handle = new Rectangle
            {
                Width = HandleHitWidth,
                Fill = idleFill,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(-HandleHitWidth / 2, 0, 0, 0),
                Tag = ("col", i),
            };
            Grid.SetColumn(handle, i);
            Grid.SetRow(handle, 0);
            Grid.SetRowSpan(handle, grid.Rows);
            handle.PointerPressed += OnBoundaryPointerPressed;
            handle.PointerEntered += (_, _) => handle.Fill = hoverFill;
            handle.PointerExited += (_, _) => handle.Fill = idleFill;
            CanvasGrid.Children.Add(handle);
        }

        // 行境界ハンドル: 行 i-1 と行 i の境界 = row=i セルの上端中心に配置
        for (var i = 1; i < grid.Rows; i++)
        {
            var handle = new Rectangle
            {
                Height = HandleHitWidth,
                Fill = idleFill,
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -HandleHitWidth / 2, 0, 0),
                Tag = ("row", i),
            };
            Grid.SetColumn(handle, 0);
            Grid.SetColumnSpan(handle, grid.Cols);
            Grid.SetRow(handle, i);
            handle.PointerPressed += OnBoundaryPointerPressed;
            handle.PointerEntered += (_, _) => handle.Fill = hoverFill;
            handle.PointerExited += (_, _) => handle.Fill = idleFill;
            CanvasGrid.Children.Add(handle);
        }
    }

    /// <summary>
    /// 境界ハンドルがダブルクリックされた時、アクティブ配置（<see cref="GridWorkspaceViewModel.SelectedPlacement"/>）の
    /// 占有列群/行群の左右枠/上下枠と一致すれば、列幅/行高を画像の実描画矩形に合わせて縮める。
    /// 一致しない境界（別の配置の枠など）では何もしない。
    /// </summary>
    private async void TryFitGridWeight(string axis, int idx)
    {
        if (_vm?.SelectedPlacement is not PlacementItemViewModel placement) return;

        var isCol = axis == "col";
        if (isCol)
        {
            var leftBoundary = placement.GridX;
            var rightBoundary = placement.GridX + Math.Max(1, placement.OccupyWidth);
            if (idx != leftBoundary && idx != rightBoundary) return;
        }
        else
        {
            var topBoundary = placement.GridY;
            var bottomBoundary = placement.GridY + Math.Max(1, placement.OccupyHeight);
            if (idx != topBoundary && idx != bottomBoundary) return;
        }

        await _vm.FitGridWeightAsync(
            placement.PlacementId,
            isCol ? FitAxis.Column : FitAxis.Row);
    }

    private static double ComputeBoundaryX(GridCanvasItemViewModel grid, int colIndex)
    {
        long total = 0;
        for (var k = 0; k < grid.ColWeights.Length; k++) total += grid.ColWeights[k];
        long prefix = 0;
        for (var k = 0; k < colIndex; k++) prefix += grid.ColWeights[k];
        return CanvasFixedSize * prefix / Math.Max(1L, total);
    }

    private static double ComputeBoundaryY(GridCanvasItemViewModel grid, int rowIndex)
    {
        long total = 0;
        for (var k = 0; k < grid.RowWeights.Length; k++) total += grid.RowWeights[k];
        long prefix = 0;
        for (var k = 0; k < rowIndex; k++) prefix += grid.RowWeights[k];
        return CanvasFixedSize * prefix / Math.Max(1L, total);
    }

    private void OnBoundaryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle handle || handle.Tag is not (string axis, int idx)) return;
        var grid = _vm?.CurrentGrid;
        if (grid is null) return;

        // Ctrl+クリックは「列幅/行高を画像にフィット」アクションへ。
        // ダブルクリックは Pressed で e.Handled=true を立てる以上 Tapped/DoubleTapped が発火せず
        // ClickCount でも安定しなかったため、修飾キーで明示的に分岐する。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            TryFitGridWeight(axis, idx);
            e.Handled = true;
            return;
        }

        _draggingGrid = grid;
        _draggingIsCol = axis == "col";
        _draggingBoundaryIndex = idx;
        _draggingHandle = handle;
        _dragStartWeights = _draggingIsCol ? grid.ColWeights : grid.RowWeights;
        var pos = e.GetPosition(BoundaryOverlay);
        _dragStartPos = _draggingIsCol ? pos.X : pos.Y;

        // ドラッグ中のみ BoundaryOverlay 全体を hit-test 対象にして PointerMoved/Released を確実に受ける。
        BoundaryOverlay.Background = Brushes.Transparent;

        // Avalonia は Pressed が発火した Control（= handle）に implicit capture を取る。
        // すると押下中の Move/Released は handle にしか届かず、handle の兄弟である
        // BoundaryOverlay の PointerMoved/Released が呼ばれない。結果として「ボタンを
        // 離しても確定せず、マウスがハンドルに追従し続けて 2 回目のクリックでようやく確定」
        // という非ドラッグ的挙動になる。Capture を BoundaryOverlay に張り直して回避する。
        e.Pointer.Capture(BoundaryOverlay);
        e.Handled = true;
    }

    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Capture が外部要因（フォーカス喪失等）で外れたときの保険。状態をクリアして
        // 「ハンドルだけ動いてリリースが拾えない」ゾンビ状態を防ぐ。
        _draggingGrid = null;
        _draggingHandle = null;
        BoundaryOverlay.Background = null;
    }

    /// <summary>
    /// BoundaryOverlay 上の PointerMoved。ドラッグ中（_draggingHandle != null）にのみ
    /// ハンドル位置をプレビュー移動する。ハンドルは CanvasGrid 内に置かれ、
    /// Margin 経由でセル境界からの相対オフセットを動かす。
    /// </summary>
    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingGrid is null || _draggingHandle is null) return;

        var pos = e.GetPosition(BoundaryOverlay);
        var current = _draggingIsCol ? pos.X : pos.Y;
        var deltaPx = current - _dragStartPos;

        // ハンドルの Margin はセル境界中心（負の HandleHitWidth/2）+ ドラッグ差分
        var baseOffset = -HandleHitWidth / 2;
        if (_draggingIsCol)
            _draggingHandle.Margin = new Thickness(baseOffset + deltaPx, 0, 0, 0);
        else
            _draggingHandle.Margin = new Thickness(0, baseOffset + deltaPx, 0, 0);
    }

    /// <summary>
    /// BoundaryOverlay 上の PointerReleased。ドラッグ確定 → 重み再計算 → UseCase 実行。
    /// </summary>
    private async void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingGrid is null || _draggingHandle is null)
        {
            // ドラッグ中でない（ハンドル外でクリック等）→ 念のため hit-test を解除
            BoundaryOverlay.Background = null;
            return;
        }

        var isCol = _draggingIsCol;
        var idx = _draggingBoundaryIndex;
        var startWeights = _dragStartWeights;

        var pos = e.GetPosition(BoundaryOverlay);
        var current = isCol ? pos.X : pos.Y;
        var deltaPx = current - _dragStartPos;

        // ドラッグ状態クリア（再 Rebuild されるため）
        _draggingGrid = null;
        _draggingHandle = null;
        BoundaryOverlay.Background = null; // 通常時は hit-test を子の Rectangle にだけ任せる
        e.Handled = true;

        if (Math.Abs(deltaPx) < 1.0) return;

        var newWeights = WeightRedistributor.Redistribute(startWeights, idx, deltaPx, CanvasFixedSize);
        if (newWeights.SequenceEqual(startWeights)) return;

        if (_vm is null) return;
        await _vm.ApplyGridWeightsAsync(
            colWeights: isCol ? newWeights : null,
            rowWeights: isCol ? null : newWeights);
    }

    /// <summary>
    /// PlacementItem の PixelOffsetX/Y を View 上のピクセル座標に換算して、
    /// Border 内部の <see cref="Image"/> の <see cref="TransformGroup"/> に
    /// <see cref="TranslateTransform"/> として加算する。
    /// Border 自体は移動させず、ClipToBounds=true により画像のはみ出しはセル境界で
    /// クリップされる（Renderer の <c>SKCanvas.ClipRect</c> と整合）。
    /// </summary>
    private void ApplyPixelOffsetTransform(Border container, PlacementItemViewModel placement)
    {
        // Border 自体は動かさない。前バージョンで Border に設定された transform があれば消す。
        container.RenderTransform = null;

        if (container.Child is not Image image)
            return; // Label fallback には適用しない

        // BuildPlacementTransform で TransformGroup（Flip/Rotate）が設定されている前提。
        var group = image.RenderTransform as TransformGroup;
        if (group is null)
        {
            group = new TransformGroup();
            image.RenderTransform = group;
        }

        // 既存の TranslateTransform（過去の更新で追加されたもの）を削除して付け直す。
        for (var i = group.Children.Count - 1; i >= 0; i--)
        {
            if (group.Children[i] is TranslateTransform)
                group.Children.RemoveAt(i);
        }

        var grid = _vm?.CurrentGrid;
        var viewW = CanvasGrid.Bounds.Width;
        var viewH = CanvasGrid.Bounds.Height;

        if (grid is null || grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0
            || viewW <= 0 || viewH <= 0
            || (placement.PixelOffsetX == 0 && placement.PixelOffsetY == 0))
        {
            return;
        }

        var sx = viewW / grid.CanvasWidth;
        var sy = viewH / grid.CanvasHeight;
        group.Children.Add(new TranslateTransform(
            placement.PixelOffsetX * sx,
            placement.PixelOffsetY * sy));
    }

    private Border BuildPlacementVisual(PlacementItemViewModel placement, bool isSelected)
    {
        var defaultBackground = isSelected
            ? (IBrush)new SolidColorBrush(Color.FromArgb(0x66, 0x33, 0x99, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));

        Control content;
        if (!string.IsNullOrEmpty(placement.ThumbnailPath) && File.Exists(placement.ThumbnailPath))
        {
            try
            {
                // 回転・反転は事前に Bitmap に焼き込む（renderer の ApplyTransform と同じ順序）。
                // これにより Avalonia の Stretch が「回転後のアスペクト比」で計算され、
                // PNG 出力（ピクセル合成）と UI 近似の見た目が一致する。
                // ScalingMode.None だけは「サムネを元画像寸法に拡大した Bitmap」を使う。
                // Source.PixelSize が元画像寸法と一致することで Stretch.None 表示が
                // Renderer の挙動（画像 > セルならクリップ）と整合する。
                Bitmap bitmap;
                var grid = _vm?.CurrentGrid;
                if (placement.ScalingMode == ViewGrid.Core.Entities.ScalingMode.None
                    && placement.SourceWidth > 0 && placement.SourceHeight > 0
                    && grid is not null)
                {
                    var rotateSwap = placement.Rotation
                        is ViewGrid.Core.Entities.Rotation.Cw90
                        or ViewGrid.Core.Entities.Rotation.Cw270;
                    var nw = rotateSwap ? placement.SourceHeight : placement.SourceWidth;
                    var nh = rotateSwap ? placement.SourceWidth : placement.SourceHeight;
                    var maxDim = Math.Max(grid.CanvasWidth, grid.CanvasHeight);
                    bitmap = LoadAndResizeAtNativeSize(
                        placement.ThumbnailPath, placement.Rotation,
                        placement.FlipX, placement.FlipY, nw, nh, maxDim);
                }
                else
                {
                    bitmap = LoadAndPreRotateBitmap(
                        placement.ThumbnailPath, placement.Rotation, placement.FlipX, placement.FlipY);
                }
                var (stretch, direction) = MapScalingMode(placement.ScalingMode);
                var (hAlign, vAlign) = stretch == Stretch.None
                    ? MapTrimmingAnchorToAlignment(placement.TrimmingAnchor)
                    : MapAlignment(placement.Alignment);

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = stretch,
                    StretchDirection = direction,
                    HorizontalAlignment = hAlign,
                    VerticalAlignment = vAlign,
                    RenderTransform = BuildPlacementTransform(placement),
                    RenderTransformOrigin = RelativePoint.Center,
                    // Image.DesiredSize はソース Bitmap のピクセル寸法を返すため、
                    // Avalonia の Grid Star Sizing がこれを MinWidth/Height として
                    // 採用すると、列・行が重みではなく画像サイズに引きずられて拡張される
                    // 結果、ハンドル位置（Grid.SetColumn/Row 配置）と視覚境界がズレ、
                    // 重みドラッグ更新もレイアウト的に反映されない症状が出る。
                    MinWidth = 0,
                    MinHeight = 0,
                };
                content = image;
            }
            catch
            {
                content = BuildLabelFallback(placement);
            }
        }
        else
        {
            content = BuildLabelFallback(placement);
        }

        var defaultBorderBrush = isSelected ? (IBrush)Brushes.DodgerBlue : Brushes.DimGray;
        var defaultBorderThickness = new Thickness(isSelected ? 2 : 1);

        var container = new Border
        {
            BorderBrush = defaultBorderBrush,
            BorderThickness = defaultBorderThickness,
            Background = defaultBackground,
            Margin = new Thickness(2),
            Child = content,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Tag = placement,
            ClipToBounds = true, // Stretch.None で大きい画像がはみ出さないように
            // Border 自身も子の Image DesiredSize に引っ張られないよう MinSize=0 を強制。
            MinWidth = 0,
            MinHeight = 0,
        };

        _placementVisualOriginals[container] = (defaultBorderBrush, defaultBorderThickness, defaultBackground);

        // 移動ドラッグソース
        container.PointerPressed += OnPlacementPointerPressed;
        container.PointerMoved += OnPlacementPointerMoved;
        container.PointerReleased += OnPlacementPointerReleased;

        // 入れ替えのドロップターゲット
        DragDrop.SetAllowDrop(container, true);
        container.AddHandler(DragDrop.DragOverEvent, OnPlacementDragOver);
        container.AddHandler(DragDrop.DragLeaveEvent, OnPlacementDragLeave);
        container.AddHandler(DragDrop.DropEvent, OnPlacementDrop);

        return container;
    }

    // ---------- 画像特性 → Avalonia 表示パラメータのマッピング ----------

    private static (Stretch Stretch, StretchDirection Direction) MapScalingMode(
        ViewGrid.Core.Entities.ScalingMode mode) => mode switch
    {
        ViewGrid.Core.Entities.ScalingMode.None => (Stretch.None, StretchDirection.Both),
        ViewGrid.Core.Entities.ScalingMode.UniformContain => (Stretch.Uniform, StretchDirection.Both),
        ViewGrid.Core.Entities.ScalingMode.UniformContainShrinkOnly => (Stretch.Uniform, StretchDirection.DownOnly),
        ViewGrid.Core.Entities.ScalingMode.UniformContainEnlargeOnly => (Stretch.Uniform, StretchDirection.UpOnly),
        ViewGrid.Core.Entities.ScalingMode.UniformCover => (Stretch.UniformToFill, StretchDirection.Both),
        ViewGrid.Core.Entities.ScalingMode.Fill => (Stretch.Fill, StretchDirection.Both),
        _ => (Stretch.Uniform, StretchDirection.Both),
    };

    private static (HorizontalAlignment H, VerticalAlignment V) MapAlignment(
        ViewGrid.Core.Entities.Alignment alignment)
    {
        var h = alignment.X switch
        {
            ViewGrid.Core.Entities.AnchorX.Left => HorizontalAlignment.Left,
            ViewGrid.Core.Entities.AnchorX.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        var v = alignment.Y switch
        {
            ViewGrid.Core.Entities.AnchorY.Top => VerticalAlignment.Top,
            ViewGrid.Core.Entities.AnchorY.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        return (h, v);
    }

    private static (HorizontalAlignment H, VerticalAlignment V) MapTrimmingAnchorToAlignment(
        ViewGrid.Core.Entities.TrimmingAnchor anchor)
    {
        var h = anchor.X switch
        {
            ViewGrid.Core.Entities.AnchorX.Left => HorizontalAlignment.Left,
            ViewGrid.Core.Entities.AnchorX.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        var v = anchor.Y switch
        {
            ViewGrid.Core.Entities.AnchorY.Top => VerticalAlignment.Top,
            ViewGrid.Core.Entities.AnchorY.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        return (h, v);
    }

    /// <summary>
    /// 配置に適用する RenderTransform。回転・反転は <see cref="LoadAndPreRotateBitmap"/> で
    /// Bitmap に焼き込み済みなので、ここでは PixelOffset 用の TranslateTransform を後で追加できる
    /// 空の TransformGroup だけを返す（<see cref="ApplyPixelOffsetTransform"/> が積む）。
    /// </summary>
    private static TransformGroup BuildPlacementTransform(PlacementItemViewModel placement)
        => new();

    /// <summary>
    /// サムネイルを読み込み、<see cref="Rotation"/> と <see cref="bool"/> Flip を SkiaSharp で
    /// 焼き込んだ Avalonia <see cref="Bitmap"/> を返す。renderer の ApplyTransform と同じ
    /// 適用順序（Flip → Rotate）で計算する。
    /// </summary>
    private static Bitmap LoadAndPreRotateBitmap(
        string thumbnailPath, ViewGrid.Core.Entities.Rotation rotation, bool flipX, bool flipY)
    {
        if (rotation == ViewGrid.Core.Entities.Rotation.None && !flipX && !flipY)
        {
            // 変換不要なら直接 Avalonia.Bitmap で読み込む（最速パス）。
            using var stream = File.OpenRead(thumbnailPath);
            return new Bitmap(stream);
        }

        using var skBitmap = SKBitmap.Decode(thumbnailPath);
        using var transformed = ApplySkiaTransform(skBitmap, rotation, flipX, flipY);
        using var skImage = SKImage.FromBitmap(transformed);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(encoded.ToArray());
        return new Bitmap(ms);
    }

    /// <summary>
    /// ScalingMode.None 用: サムネを「元画像と同じピクセル寸法（または最大寸法に丸めたサイズ）」の
    /// Bitmap として返す。これにより View の <see cref="Stretch.None"/> 表示が
    /// Renderer の元画像基準と一致し、画像 &gt; セルのケースでセル全体を埋めてクリップされる挙動になる。
    /// メモリ消費を抑えるため、<paramref name="maxDim"/> を超える寸法は等比で縮小する。
    /// </summary>
    private static Bitmap LoadAndResizeAtNativeSize(
        string thumbnailPath, ViewGrid.Core.Entities.Rotation rotation, bool flipX, bool flipY,
        int nativeWidth, int nativeHeight, int maxDim)
    {
        // 上限クランプ（アスペクト維持）
        var maxNative = Math.Max(nativeWidth, nativeHeight);
        if (maxDim > 0 && maxNative > maxDim)
        {
            var ratio = (double)maxDim / maxNative;
            nativeWidth = Math.Max(1, (int)Math.Round(nativeWidth * ratio));
            nativeHeight = Math.Max(1, (int)Math.Round(nativeHeight * ratio));
        }

        using var skBitmap = SKBitmap.Decode(thumbnailPath);
        using var transformed = ApplySkiaTransform(skBitmap, rotation, flipX, flipY);

        using var resized = new SKBitmap(
            nativeWidth, nativeHeight, transformed.ColorType, transformed.AlphaType);
        using (var canvas = new SKCanvas(resized))
        using (var sourceImage = SKImage.FromBitmap(transformed))
        {
            canvas.Clear(SKColors.Transparent);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            canvas.DrawImage(sourceImage,
                SKRect.Create(0, 0, transformed.Width, transformed.Height),
                SKRect.Create(0, 0, nativeWidth, nativeHeight),
                sampling);
        }

        using var skImage = SKImage.FromBitmap(resized);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(encoded.ToArray());
        return new Bitmap(ms);
    }

    private static SKBitmap ApplySkiaTransform(
        SKBitmap source, ViewGrid.Core.Entities.Rotation rotation, bool flipX, bool flipY)
    {
        var rotateSwap = rotation is ViewGrid.Core.Entities.Rotation.Cw90
            or ViewGrid.Core.Entities.Rotation.Cw270;
        var dstW = rotateSwap ? source.Height : source.Width;
        var dstH = rotateSwap ? source.Width : source.Height;
        var dst = new SKBitmap(dstW, dstH, source.ColorType, source.AlphaType);
        try
        {
            using var canvas = new SKCanvas(dst);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(dstW / 2f, dstH / 2f);
            canvas.RotateDegrees((int)rotation);
            canvas.Scale(flipX ? -1f : 1f, flipY ? -1f : 1f);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            canvas.DrawBitmap(source, 0, 0);
            return dst;
        }
        catch
        {
            dst.Dispose();
            throw;
        }
    }

    private static TextBlock BuildLabelFallback(PlacementItemViewModel placement) => new()
    {
        Text = placement.Label,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 10,
        Opacity = 0.85,
    };

    // ---------- 配置済み Border のドラッグソース ----------

    private void OnPlacementPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not PlacementItemViewModel placement)
            return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;

        // Shift 押下中はピクセル微調整モード。通常の D&D を抑止して PixelOffsetX/Y を
        // ドラッグで連続更新する。Avalonia の implicit pointer capture を border に
        // 明示的に張り直して、押下中の Move/Released を確実に同 border で受ける。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // 対象 placement を選択状態にして Inspector を当該画像に切り替える。
            // Inspector は source の PixelOffset 変更を購読しているので、ドラッグ中も
            // リアルタイムで数値が追従する。
            if (_vm is not null && _vm.SelectedPlacement?.PlacementId != placement.PlacementId)
                _vm.SelectedPlacement = placement;

            _pixelOffsetDragging = true;
            // CanvasGrid の論理座標（Viewbox 内、固定 600×600）で測る。Viewbox が拡縮しても
            // 論理座標は不変なので、表示倍率に依存しない換算ができる。
            _pixelOffsetStart = e.GetPosition(CanvasGrid);
            _pixelOffsetStartX = placement.PixelOffsetX;
            _pixelOffsetStartY = placement.PixelOffsetY;
            _pixelOffsetTarget = placement;
            _pixelOffsetBorder = border;
            e.Pointer.Capture(border);
            e.Handled = true;
            return;
        }

        // 押下時点では選択を更新しない（Rebuild が押下中の Border を破棄して
        // PointerMoved が届かなくなる事象を避けるため）。
        _placementPressOrigin = e.GetPosition(this);
        _placementPressItem = placement;
        _placementPressEvent = e;
        _placementPressBorder = border;
    }

    private async void OnPlacementPointerMoved(object? sender, PointerEventArgs e)
    {
        // Shift+ドラッグ中: PixelOffset を即時更新してプレビュー反映（DB 永続化は Released 時）
        if (_pixelOffsetDragging && _pixelOffsetTarget is not null && _pixelOffsetBorder is not null)
        {
            UpdatePixelOffsetFromDrag(e);
            return;
        }

        if (_placementPressOrigin is null || _placementPressItem is null || _placementPressEvent is null)
            return;

        var current = e.GetPosition(this);
        var dx = Math.Abs(current.X - _placementPressOrigin.Value.X);
        var dy = Math.Abs(current.Y - _placementPressOrigin.Value.Y);
        if (dx < DragThreshold && dy < DragThreshold)
            return;

        var item = _placementPressItem;
        var trigger = _placementPressEvent;
        var border = _placementPressBorder;
        ResetPlacementPressState();

        // 掴んだセルのオフセットを計算（NxM 配置の右下端を掴んでドラッグした場合、
        // ドロップ位置から (-W+1, -H+1) ずれた位置が新左上になる）。
        var (ox, oy) = ComputeGrabOffset(border, trigger, item);

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText($"{PlacementPrefix}{item.PlacementId}:{ox},{oy}"));

        try
        {
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
        }
        catch
        {
            // ユーザー操作起点の例外は握りつぶす
        }
    }

    private void OnPlacementPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Shift+ドラッグの終了: ドラッグ中に PlacementItemViewModel.PixelOffsetX/Y を直接更新済み。
        // ここでは状態クリアのみ。Inspector が PropertyChanged 経由で IsDirty=true を立て、
        // ユーザーが「保存」ボタンを押したときに DB 永続化される（Inspector 編集と挙動を統一）。
        if (_pixelOffsetDragging)
        {
            _pixelOffsetDragging = false;
            _pixelOffsetTarget = null;
            _pixelOffsetBorder = null;
            e.Handled = true;
            return;
        }

        // 閾値を超えずに離した場合 = クリック → ここで選択を確定する。
        if (_placementPressItem is not null && _vm is not null)
            _vm.SelectedPlacement = _placementPressItem;

        ResetPlacementPressState();
    }

    /// <summary>
    /// Shift+ドラッグ中の PointerMoved 処理。マウスの delta を「キャンバス座標系の
    /// ピクセル値」に換算して <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> を更新し、<see cref="ApplyPixelOffsetTransform"/> を呼んで即時再描画。
    /// 永続化は Released 時にまとめて行う（毎フレーム DB に書かない）。
    /// 座標は <see cref="CanvasGrid"/> の論理座標（固定 <see cref="CanvasFixedSize"/>）で
    /// 測るので、Viewbox の拡縮倍率に依存せず一貫した換算ができる。
    /// </summary>
    private void UpdatePixelOffsetFromDrag(PointerEventArgs e)
    {
        var grid = _vm?.CurrentGrid;
        if (grid is null || grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0)
            return;

        var current = e.GetPosition(CanvasGrid);
        var dx = current.X - _pixelOffsetStart.X;
        var dy = current.Y - _pixelOffsetStart.Y;

        // 論理 600×600 上の delta を「キャンバス CanvasWidth×CanvasHeight」上の
        // ピクセル量に換算（見たまま動くスケール）。
        var sx = grid.CanvasWidth / CanvasFixedSize;
        var sy = grid.CanvasHeight / CanvasFixedSize;
        var max = PlacementInspectorViewModel.MaxPixelOffset;
        var newX = Math.Clamp(_pixelOffsetStartX + (int)Math.Round(dx * sx), -max, max);
        var newY = Math.Clamp(_pixelOffsetStartY + (int)Math.Round(dy * sy), -max, max);

        var target = _pixelOffsetTarget!;
        target.PixelOffsetX = newX;
        target.PixelOffsetY = newY;
        ApplyPixelOffsetTransform(_pixelOffsetBorder!, target);
    }

    private void ResetPlacementPressState()
    {
        _placementPressOrigin = null;
        _placementPressItem = null;
        _placementPressEvent = null;
        _placementPressBorder = null;
    }

    private static (int Ox, int Oy) ComputeGrabOffset(
        Border? border, PointerPressedEventArgs? trigger, PlacementItemViewModel item)
    {
        if (border is null || trigger is null) return (0, 0);
        var w = Math.Max(1, item.OccupyWidth);
        var h = Math.Max(1, item.OccupyHeight);
        if (w == 1 && h == 1) return (0, 0);

        var local = trigger.GetPosition(border);
        var bw = border.Bounds.Width;
        var bh = border.Bounds.Height;
        if (bw <= 0 || bh <= 0) return (0, 0);

        var cellW = bw / w;
        var cellH = bh / h;
        var ox = Math.Clamp((int)(local.X / cellW), 0, w - 1);
        var oy = Math.Clamp((int)(local.Y / cellH), 0, h - 1);
        return (ox, oy);
    }

    // ---------- ハイライトブラシ ----------

    private static readonly IBrush DragOverValidBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x33, 0xFF, 0x66));
    private static readonly IBrush DragOverInvalidBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x44, 0x44));
    private static readonly IBrush DragOverSwapBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xCC, 0x33));
    private static readonly IBrush PlacementSwapBorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xCC, 0x00));
    private static readonly IBrush PlacementInvalidBorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x33, 0x33));

    // ---------- セル DragOver/Drop ----------

    private void OnCellDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border cell)
            return;

        if (!e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);

        if (src.Kind == DragKind.Unknown || cell.Tag is not CellPosition pos)
        {
            ClearAllCellHighlights();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var occupy = src.OccupySize ?? OccupySize.OneByOne;
        var newTopLeftX = pos.X - src.Offset.X;
        var newTopLeftY = pos.Y - src.Offset.Y;
        var (cellsToHighlight, isValid) = AnalyzeHoverRangeRaw(newTopLeftX, newTopLeftY, occupy, src);

        ClearAllCellHighlights();

        // 境界外や重複は Placement ドラッグでも一律「不可（赤）」とする。
        // Swap の黄色ハイライトは「配置済み Border を直接ホバーしたとき」だけに限定する
        // （その経路は OnPlacementDragOver で処理される）。
        var brush = isValid ? DragOverValidBrush : DragOverInvalidBrush;

        foreach (var c in cellsToHighlight)
        {
            if (_cellBorders.TryGetValue(c, out var border))
                border.Background = brush;
        }

        e.DragEffects = src.Kind switch
        {
            DragKind.Copy when isValid => DragDropEffects.Copy,
            DragKind.Placement when isValid => DragDropEffects.Move,
            _ => DragDropEffects.None,
        };
        e.Handled = true;
    }

    private void OnCellDragLeave(object? sender, DragEventArgs e)
    {
        // hover が外れた瞬間でも他セルがまだホバーしている可能性があるため、
        // セル単位ではなく一括クリアは Drop / 別セル DragOver 時に実施する。
        if (sender is Border cell)
            cell.Background = Brushes.Transparent;
    }

    private async void OnCellDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border cell)
            return;

        ClearAllCellHighlights();
        e.Handled = true;

        if (cell.Tag is not CellPosition position || _vm is null)
            return;

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);
        var corrected = ApplyOffset(position, src.Offset);
        if (corrected is null)
            return;

        await DispatchPositionedDropAsync(src, corrected.Value);
    }

    // ---------- 配置済み Border DragOver/Drop ----------

    private void OnPlacementDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not PlacementItemViewModel target)
            return;

        if (!e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);

        switch (src.Kind)
        {
            case DragKind.Copy:
                ApplyPlacementHighlight(border, PlacementInvalidBorderBrush, DragOverInvalidBrush);
                e.DragEffects = DragDropEffects.None;
                break;

            case DragKind.Placement when src.PlacementSource?.PlacementId == target.PlacementId:
                // 自分自身の Border 上だが、NxM 配置を「元位置と部分重複する位置」に移す操作
                // をサポートするため、マウス位置のセルを移動先候補として扱う。
                HandleSelfPlacementHover(e, src);
                break;

            case DragKind.Placement:
                ApplyPlacementHighlight(border, PlacementSwapBorderBrush, DragOverSwapBrush);
                e.DragEffects = DragDropEffects.Move;
                break;

            default:
                e.DragEffects = DragDropEffects.None;
                break;
        }
        e.Handled = true;
    }

    private void HandleSelfPlacementHover(DragEventArgs e, DragSourceInfo src)
    {
        var cellPos = ResolveCellAtPointer(e);
        if (cellPos is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var occupy = src.OccupySize ?? OccupySize.OneByOne;
        var newTopLeftX = cellPos.Value.X - src.Offset.X;
        var newTopLeftY = cellPos.Value.Y - src.Offset.Y;
        var (cellsToHighlight, isValid) = AnalyzeHoverRangeRaw(newTopLeftX, newTopLeftY, occupy, src);

        ClearAllCellHighlights();
        var brush = isValid ? DragOverValidBrush : DragOverInvalidBrush;
        foreach (var c in cellsToHighlight)
        {
            if (_cellBorders.TryGetValue(c, out var b))
                b.Background = brush;
        }

        e.DragEffects = isValid ? DragDropEffects.Move : DragDropEffects.None;
    }

    private CellPosition? ResolveCellAtPointer(DragEventArgs e)
    {
        if (_vm?.CurrentGrid is not { } grid) return null;
        var local = e.GetPosition(CanvasGrid);
        var width = CanvasGrid.Bounds.Width;
        var height = CanvasGrid.Bounds.Height;
        if (width <= 0 || height <= 0) return null;
        if (local.X < 0 || local.Y < 0 || local.X >= width || local.Y >= height) return null;

        var cellWidth = width / grid.Cols;
        var cellHeight = height / grid.Rows;
        var col = Math.Clamp((int)(local.X / cellWidth), 0, grid.Cols - 1);
        var row = Math.Clamp((int)(local.Y / cellHeight), 0, grid.Rows - 1);
        return new CellPosition(col, row);
    }

    private void OnPlacementDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
            RestorePlacementHighlight(border);
    }

    private async void OnPlacementDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border border)
            return;

        RestorePlacementHighlight(border);
        ClearAllCellHighlights();
        e.Handled = true;

        if (border.Tag is not PlacementItemViewModel target || _vm is null)
            return;

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);

        // 自分自身の上にドロップした場合はマウス位置のセルを基準に offset 補正する
        // （NxM 配置の「ずらし移動」をサポート）。
        if (src.Kind == DragKind.Placement && src.PlacementSource?.PlacementId == target.PlacementId)
        {
            var cellPos = ResolveCellAtPointer(e);
            if (cellPos is null) return;
            var corrected = ApplyOffset(cellPos.Value, src.Offset);
            if (corrected is null) return;
            await DispatchPositionedDropAsync(src, corrected.Value);
            return;
        }

        // 別配置上 = swap：target.Position をそのまま使う（offset 補正は適用しない）。
        await DispatchPositionedDropAsync(src, new CellPosition(target.GridX, target.GridY));
    }

    private static CellPosition? ApplyOffset(CellPosition mouseCell, GrabOffset offset)
    {
        var nx = mouseCell.X - offset.X;
        var ny = mouseCell.Y - offset.Y;
        if (nx < 0 || ny < 0) return null;
        return new CellPosition(nx, ny);
    }

    private async Task DispatchPositionedDropAsync(DragSourceInfo src, CellPosition position)
    {
        if (_vm is null) return;
        switch (src.Kind)
        {
            case DragKind.Copy when src.CopySource is not null:
                await _vm.PlaceCopyAtAsync(src.CopySource.CopyId, position);
                break;
            case DragKind.Placement when src.PlacementSource is not null:
                await _vm.MoveOrSwapPlacementAsync(src.PlacementSource.PlacementId, position);
                break;
        }
    }

    private static void ApplyPlacementHighlight(Border border, IBrush borderBrush, IBrush background)
    {
        border.BorderBrush = borderBrush;
        border.BorderThickness = new Thickness(4);
        border.Background = background;
    }

    private void RestorePlacementHighlight(Border border)
    {
        if (!_placementVisualOriginals.TryGetValue(border, out var orig))
            return;
        border.BorderBrush = orig.Brush;
        border.BorderThickness = orig.Thickness;
        if (orig.Background is not null)
            border.Background = orig.Background;
    }

    private void ClearAllCellHighlights()
    {
        foreach (var border in _cellBorders.Values)
            border.Background = Brushes.Transparent;
    }

    // ---------- ヘルパ ----------

    /// <summary>
    /// hover 中セル + 占有サイズから、ハイライトすべきセル群と妥当性を返す。
    /// </summary>
    private (IReadOnlyList<CellPosition> Cells, bool IsValid) AnalyzeHoverRange(
        CellPosition origin, OccupySize occupy, DragSourceInfo src)
        => AnalyzeHoverRangeRaw(origin.X, origin.Y, occupy, src);

    /// <summary>
    /// 左上候補（負も許容）と占有サイズから、ハイライトすべきセル群と妥当性を返す。
    /// オフセット補正で負座標が出るケースに対応するためのオーバーロード。
    /// </summary>
    private (IReadOnlyList<CellPosition> Cells, bool IsValid) AnalyzeHoverRangeRaw(
        int originX, int originY, OccupySize occupy, DragSourceInfo src)
    {
        if (_vm?.CurrentGrid is not { } grid)
            return (Array.Empty<CellPosition>(), false);

        var endX = originX + occupy.Width;
        var endY = originY + occupy.Height;
        var inBounds = originX >= 0 && originY >= 0 && endX <= grid.Cols && endY <= grid.Rows;

        var cells = new List<CellPosition>(occupy.Width * occupy.Height);
        for (var dy = 0; dy < occupy.Height; dy++)
        {
            for (var dx = 0; dx < occupy.Width; dx++)
            {
                var x = originX + dx;
                var y = originY + dy;
                if (x >= 0 && x < grid.Cols && y >= 0 && y < grid.Rows)
                    cells.Add(new CellPosition(x, y));
            }
        }

        if (!inBounds)
            return (cells, false);

        var conflicts = false;
        foreach (var cell in cells)
        {
            var occupant = FindOccupantPlacement(cell);
            if (occupant is null)
                continue;

            // Placement ドラッグで自分自身に重なるのは衝突扱いしない（自己除外）
            if (src.Kind == DragKind.Placement &&
                src.PlacementSource is not null &&
                occupant.PlacementId == src.PlacementSource.PlacementId)
            {
                continue;
            }

            conflicts = true;
            break;
        }

        return (cells, !conflicts);
    }

    private PlacementItemViewModel? FindOccupantPlacement(CellPosition pos)
    {
        if (_vm is null) return null;
        foreach (var p in _vm.Placements)
        {
            if (pos.X >= p.GridX && pos.X < p.GridX + Math.Max(1, p.OccupyWidth) &&
                pos.Y >= p.GridY && pos.Y < p.GridY + Math.Max(1, p.OccupyHeight))
                return p;
        }
        return null;
    }

    private DragSourceInfo ResolveDragSource(string text)
    {
        if (text.StartsWith(CopyPrefix, StringComparison.Ordinal))
        {
            var (id, offset) = ParseIdAndOffset(text[CopyPrefix.Length..]);
            if (id is not null)
            {
                var source = _vm?.Candidates.FirstOrDefault(c => c.CopyId == id.Value);
                return new DragSourceInfo(DragKind.Copy, source?.OccupySize, null, source, offset);
            }
            return new DragSourceInfo(DragKind.Copy, null, null, null, default);
        }

        if (text.StartsWith(PlacementPrefix, StringComparison.Ordinal))
        {
            var (id, offset) = ParseIdAndOffset(text[PlacementPrefix.Length..]);
            if (id is not null)
            {
                var source = _vm?.Placements.FirstOrDefault(p => p.PlacementId == id.Value);
                return new DragSourceInfo(
                    DragKind.Placement,
                    source is null ? null : new OccupySize(source.OccupyWidth, source.OccupyHeight),
                    source,
                    null,
                    offset);
            }
            return new DragSourceInfo(DragKind.Placement, null, null, null, default);
        }
        return new DragSourceInfo(DragKind.Unknown, null, null, null, default);
    }

    private static (Guid? Id, GrabOffset Offset) ParseIdAndOffset(string s)
    {
        var colonIdx = s.IndexOf(':');
        string idStr;
        var offset = default(GrabOffset);
        if (colonIdx < 0)
        {
            idStr = s;
        }
        else
        {
            idStr = s[..colonIdx];
            var rest = s[(colonIdx + 1)..];
            var commaIdx = rest.IndexOf(',');
            if (commaIdx > 0
                && int.TryParse(rest[..commaIdx], out var ox)
                && int.TryParse(rest[(commaIdx + 1)..], out var oy))
            {
                offset = new GrabOffset(ox, oy);
            }
        }
        return Guid.TryParse(idStr, out var id) ? (id, offset) : (null, default);
    }

    private readonly record struct GrabOffset(int X, int Y);

    private readonly record struct DragSourceInfo(
        DragKind Kind,
        OccupySize? OccupySize,
        PlacementItemViewModel? PlacementSource,
        CopyCandidateViewModel? CopySource,
        GrabOffset Offset);

    private enum DragKind { Unknown, Copy, Placement }

    private sealed class AnonymousObserver<T>(Action<T?> onNext) : IObserver<T?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T? value) => onNext(value);
    }
}
