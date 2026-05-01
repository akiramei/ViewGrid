using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class CopyPropertiesView : UserControl
{
    private CopyPropertiesViewModel? _vm;

    private enum DragMode
    {
        None,
        CreateNew,
        Move,
        // 4 隅: 両軸リサイズ
        ResizeNW,
        ResizeNE,
        ResizeSW,
        ResizeSE,
        // 4 辺中央: 片軸のみリサイズ
        ResizeN,
        ResizeS,
        ResizeE,
        ResizeW,
    }

    private bool _isDragging;
    private DragMode _dragMode;
    private Point _dragStartPoint;
    private (double X, double Y, double W, double H) _dragStartRect;

    private const double HandleSize = 12;
    private const double HandleHitSlack = 4;

    public CopyPropertiesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // ManualCropImage のサイズ変更（コンテナリサイズや初回 Source 設定時）で再描画。
        // 旧版は ManualCropOverlay.LayoutUpdated を購読していたが、
        // Canvas.Children.Clear/Add が InvalidateMeasure → LayoutUpdated を再発火させて
        // 無限再帰 → StackOverflow でプロセス即終了する不具合があった。
        // PropertyChanged + BoundsProperty に絞ると Children 操作では再発火しない（Image 自体の
        // レイアウトは Children 操作で変わらないため）。
        ManualCropImage.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) UpdateOverlay();
        };
    }

    /// <summary>
    /// AutoCrop の「画像クリックピッカー」用ハンドラ。サムネ <see cref="Image"/> 上の
    /// クリック座標を bitmap pixel 座標に換算して <see cref="CopyPropertiesViewModel.PickColorFromThumbnailAsync"/>
    /// を呼ぶ。<see cref="Stretch.Uniform"/> でアスペクト維持表示しているため、表示倍率と
    /// パディング（左右 or 上下の余白）を計算してクリック点を bitmap 座標に戻す。
    /// </summary>
    private async void OnColorPickerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image image) return;
        if (DataContext is not CopyPropertiesViewModel vm) return;
        if (image.Source is not Bitmap bitmap) return;
        if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(image);
        var imgW = image.Bounds.Width;
        var imgH = image.Bounds.Height;
        var bmpW = bitmap.PixelSize.Width;
        var bmpH = bitmap.PixelSize.Height;
        if (imgW <= 0 || imgH <= 0 || bmpW <= 0 || bmpH <= 0) return;

        var scale = Math.Min(imgW / bmpW, imgH / bmpH);
        var displayW = bmpW * scale;
        var displayH = bmpH * scale;
        var padX = (imgW - displayW) / 2.0;
        var padY = (imgH - displayH) / 2.0;

        var localX = pos.X - padX;
        var localY = pos.Y - padY;
        if (localX < 0 || localX >= displayW || localY < 0 || localY >= displayH) return;

        var px = (int)(localX / scale);
        var py = (int)(localY / scale);

        try
        {
            await vm.PickColorFromThumbnailAsync(px, py, bmpW, bmpH);
        }
        catch
        {
            // ユーザー操作起点の例外は握りつぶす
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as CopyPropertiesViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        UpdateOverlay();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CopyPropertiesViewModel.ManualCropPixelX)
            or nameof(CopyPropertiesViewModel.ManualCropPixelY)
            or nameof(CopyPropertiesViewModel.ManualCropPixelWidth)
            or nameof(CopyPropertiesViewModel.ManualCropPixelHeight)
            or nameof(CopyPropertiesViewModel.ManualCropEnabled)
            or nameof(CopyPropertiesViewModel.SourceWidth)
            or nameof(CopyPropertiesViewModel.SourceHeight)
            or nameof(CopyPropertiesViewModel.ThumbnailPath))
        {
            UpdateOverlay();
        }
    }

    /// <summary>
    /// 元画像ピクセル空間（VM 値）→ オーバーレイ座標空間（サムネ表示領域）の換算。
    /// サムネは Stretch.Uniform でアスペクト維持表示されているため、表示倍率とパディングを考慮。
    /// </summary>
    private (double X, double Y, double W, double H, double Scale, double PadX, double PadY)?
        GetThumbnailDisplayMetrics()
    {
        if (_vm is null) return null;
        if (_vm.SourceWidth <= 0 || _vm.SourceHeight <= 0) return null;
        if (ManualCropImage.Source is not Bitmap bmp) return null;

        var imgW = ManualCropImage.Bounds.Width;
        var imgH = ManualCropImage.Bounds.Height;
        if (imgW <= 0 || imgH <= 0) return null;

        // サムネ画像（bmp）と元画像（_vm.SourceWidth/Height）はサイズが違う場合があるが、
        // 比率は一致するはず。Stretch.Uniform 表示の倍率はサムネ Bitmap 基準で計算する。
        var bmpW = (double)bmp.PixelSize.Width;
        var bmpH = (double)bmp.PixelSize.Height;
        var scale = Math.Min(imgW / bmpW, imgH / bmpH);
        var displayW = bmpW * scale;
        var displayH = bmpH * scale;
        var padX = (imgW - displayW) / 2.0;
        var padY = (imgH - displayH) / 2.0;

        // 元画像ピクセル → オーバーレイ座標の倍率（サムネ → オーバーレイの倍率を経由）
        // 元画像ピクセル × (bmpW / SourceWidth) × scale = オーバーレイ座標
        var srcToOverlayScale = (bmpW / _vm.SourceWidth) * scale;

        return (0, 0, displayW, displayH, srcToOverlayScale, padX, padY);
    }

    /// <summary>VM の ManualCrop ピクセル値からオーバーレイ座標 (x, y, w, h) を返す。</summary>
    private (double X, double Y, double W, double H)? ComputeOverlayRect()
    {
        if (_vm is null) return null;
        var m = GetThumbnailDisplayMetrics();
        if (m is null) return null;
        var (_, _, _, _, scale, padX, padY) = m.Value;
        var x = padX + _vm.ManualCropPixelX * scale;
        var y = padY + _vm.ManualCropPixelY * scale;
        var w = _vm.ManualCropPixelWidth * scale;
        var h = _vm.ManualCropPixelHeight * scale;
        return (x, y, w, h);
    }

    /// <summary>オーバーレイ座標 (px, py) を元画像ピクセル座標に逆変換。</summary>
    private (double SrcX, double SrcY)? OverlayToSource(double overlayX, double overlayY)
    {
        var m = GetThumbnailDisplayMetrics();
        if (m is null) return null;
        var (_, _, _, _, scale, padX, padY) = m.Value;
        if (scale <= 0) return null;
        return ((overlayX - padX) / scale, (overlayY - padY) / scale);
    }

    /// <summary>
    /// マット 4 領域 + 矩形 + 4 隅ハンドル + 4 辺中央ハンドルを Canvas 上に再描画する。
    /// VM 値変更時 / Image レイアウト変更時に呼ばれる。
    /// LayoutUpdated は再帰の原因になるため購読しない（Phase 5 のクラッシュ修正済み）。
    /// </summary>
    private void UpdateOverlay()
    {
        if (ManualCropOverlay is null) return;
        ManualCropOverlay.Children.Clear();

        if (_vm is null || !_vm.ManualCropEnabled || !_vm.IsManualCropDefined) return;

        var metrics = GetThumbnailDisplayMetrics();
        if (metrics is null) return;
        var (_, _, displayW, displayH, _, padX, padY) = metrics.Value;

        var rect = ComputeOverlayRect();
        if (rect is not { } r) return;
        if (r.W <= 0 || r.H <= 0) return;

        // マット 4 領域: サムネ表示領域 (padX, padY, displayW, displayH) のうち、
        // 矩形外側を半透明黒で覆って選択範囲を強調する。座標系はオーバーレイ Canvas。
        var mattBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        AddMatt(padX, padY, displayW, r.Y - padY, mattBrush);                          // 上
        AddMatt(padX, r.Y + r.H, displayW, padY + displayH - (r.Y + r.H), mattBrush); // 下
        AddMatt(padX, r.Y, r.X - padX, r.H, mattBrush);                               // 左
        AddMatt(r.X + r.W, r.Y, padX + displayW - (r.X + r.W), r.H, mattBrush);       // 右

        // 矩形枠（マットで内部は明るく見える形）
        var border = new Rectangle
        {
            Width = r.W,
            Height = r.H,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);
        ManualCropOverlay.Children.Add(border);

        // 4 隅ハンドル（両軸リサイズ）
        AddHandle(r.X, r.Y);
        AddHandle(r.X + r.W, r.Y);
        AddHandle(r.X, r.Y + r.H);
        AddHandle(r.X + r.W, r.Y + r.H);

        // 4 辺中央ハンドル（片軸リサイズ）
        AddHandle(r.X + r.W / 2, r.Y);
        AddHandle(r.X + r.W / 2, r.Y + r.H);
        AddHandle(r.X, r.Y + r.H / 2);
        AddHandle(r.X + r.W, r.Y + r.H / 2);
    }

    private void AddMatt(double x, double y, double w, double h, IBrush brush)
    {
        if (w <= 0 || h <= 0) return;
        var rect = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        ManualCropOverlay.Children.Add(rect);
    }

    private void AddHandle(double cx, double cy)
    {
        var h = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            Stroke = Brushes.White,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(h, cx - HandleSize / 2);
        Canvas.SetTop(h, cy - HandleSize / 2);
        ManualCropOverlay.Children.Add(h);
    }

    /// <summary>クリック点が 4 隅 / 4 辺中央のハンドルに当たれば、対応するリサイズモードを返す。
    /// 4 隅優先（隅と辺が重なる位置では隅のリサイズを優先、両軸変えられる方が直感的）。</summary>
    private static DragMode HitTestHandle(Point p, (double X, double Y, double W, double H) r)
    {
        var slack = HandleSize / 2 + HandleHitSlack;

        // 4 隅: 両軸リサイズ
        if (Distance(p, new Point(r.X, r.Y)) <= slack) return DragMode.ResizeNW;
        if (Distance(p, new Point(r.X + r.W, r.Y)) <= slack) return DragMode.ResizeNE;
        if (Distance(p, new Point(r.X, r.Y + r.H)) <= slack) return DragMode.ResizeSW;
        if (Distance(p, new Point(r.X + r.W, r.Y + r.H)) <= slack) return DragMode.ResizeSE;

        // 4 辺中央: 片軸リサイズ
        if (Distance(p, new Point(r.X + r.W / 2, r.Y)) <= slack) return DragMode.ResizeN;
        if (Distance(p, new Point(r.X + r.W / 2, r.Y + r.H)) <= slack) return DragMode.ResizeS;
        if (Distance(p, new Point(r.X, r.Y + r.H / 2)) <= slack) return DragMode.ResizeW;
        if (Distance(p, new Point(r.X + r.W, r.Y + r.H / 2)) <= slack) return DragMode.ResizeE;

        return DragMode.None;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsInsideRect(Point p, (double X, double Y, double W, double H) r)
        => p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H;

    private void OnManualCropOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || !_vm.ManualCropEnabled) return;
        if (!e.GetCurrentPoint(ManualCropOverlay).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(ManualCropOverlay);

        // 既存矩形あり → ハンドル / 内部 / 外部の判定
        if (_vm.IsManualCropDefined && ComputeOverlayRect() is { } existing)
        {
            var hit = HitTestHandle(pos, existing);
            if (hit != DragMode.None)
            {
                _dragMode = hit;
            }
            else if (IsInsideRect(pos, existing))
            {
                _dragMode = DragMode.Move;
            }
            else
            {
                _dragMode = DragMode.CreateNew;
            }
        }
        else
        {
            _dragMode = DragMode.CreateNew;
        }

        _isDragging = true;
        _dragStartPoint = pos;
        _dragStartRect = (_vm.ManualCropPixelX, _vm.ManualCropPixelY,
                          _vm.ManualCropPixelWidth, _vm.ManualCropPixelHeight);
        e.Pointer.Capture(ManualCropOverlay);
        e.Handled = true;
    }

    private void OnManualCropOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm is null) return;
        var pos = e.GetPosition(ManualCropOverlay);
        ApplyDragUpdate(pos);
    }

    private void OnManualCropOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// PointerMoved 中の VM 値更新。各モードに応じてオーバーレイ座標 → 元画像ピクセルに換算し、
    /// VM の ManualCropPixelX/Y/W/H をクランプ付きで更新する。
    /// </summary>
    private void ApplyDragUpdate(Point pos)
    {
        if (_vm is null) return;
        if (_vm.SourceWidth <= 0 || _vm.SourceHeight <= 0) return;

        var srcStart = OverlayToSource(_dragStartPoint.X, _dragStartPoint.Y);
        var srcCurrent = OverlayToSource(pos.X, pos.Y);
        if (srcStart is null || srcCurrent is null) return;

        var sw = _vm.SourceWidth;
        var sh = _vm.SourceHeight;

        switch (_dragMode)
        {
            case DragMode.CreateNew:
            {
                var x1 = Math.Clamp(srcStart.Value.SrcX, 0, sw);
                var y1 = Math.Clamp(srcStart.Value.SrcY, 0, sh);
                var x2 = Math.Clamp(srcCurrent.Value.SrcX, 0, sw);
                var y2 = Math.Clamp(srcCurrent.Value.SrcY, 0, sh);
                _vm.ManualCropPixelX = Math.Min(x1, x2);
                _vm.ManualCropPixelY = Math.Min(y1, y2);
                _vm.ManualCropPixelWidth = Math.Abs(x2 - x1);
                _vm.ManualCropPixelHeight = Math.Abs(y2 - y1);
                break;
            }
            case DragMode.Move:
            {
                var dx = srcCurrent.Value.SrcX - srcStart.Value.SrcX;
                var dy = srcCurrent.Value.SrcY - srcStart.Value.SrcY;
                var newX = Math.Clamp(_dragStartRect.X + dx, 0, sw - _dragStartRect.W);
                var newY = Math.Clamp(_dragStartRect.Y + dy, 0, sh - _dragStartRect.H);
                _vm.ManualCropPixelX = newX;
                _vm.ManualCropPixelY = newY;
                break;
            }
            case DragMode.ResizeNW:
            case DragMode.ResizeNE:
            case DragMode.ResizeSW:
            case DragMode.ResizeSE:
            {
                // 4 隅: 両軸リサイズ。対角の隅を fixed として、ドラッグ点との bbox を作る。
                var startX = _dragStartRect.X;
                var startY = _dragStartRect.Y;
                var startRight = _dragStartRect.X + _dragStartRect.W;
                var startBottom = _dragStartRect.Y + _dragStartRect.H;

                var fixedX = (_dragMode is DragMode.ResizeNE or DragMode.ResizeSE) ? startX : startRight;
                var fixedY = (_dragMode is DragMode.ResizeSW or DragMode.ResizeSE) ? startY : startBottom;
                var moveX = Math.Clamp(srcCurrent.Value.SrcX, 0, sw);
                var moveY = Math.Clamp(srcCurrent.Value.SrcY, 0, sh);

                _vm.ManualCropPixelX = Math.Min(fixedX, moveX);
                _vm.ManualCropPixelY = Math.Min(fixedY, moveY);
                _vm.ManualCropPixelWidth = Math.Abs(moveX - fixedX);
                _vm.ManualCropPixelHeight = Math.Abs(moveY - fixedY);
                break;
            }
            case DragMode.ResizeN:
            case DragMode.ResizeS:
            {
                // 上辺/下辺: Y 軸のみリサイズ。X / W は startRect そのまま、対辺を fixed Y として bbox 作る。
                var startY = _dragStartRect.Y;
                var startBottom = _dragStartRect.Y + _dragStartRect.H;
                var fixedY = _dragMode is DragMode.ResizeN ? startBottom : startY;
                var moveY = Math.Clamp(srcCurrent.Value.SrcY, 0, sh);

                _vm.ManualCropPixelX = _dragStartRect.X;
                _vm.ManualCropPixelWidth = _dragStartRect.W;
                _vm.ManualCropPixelY = Math.Min(fixedY, moveY);
                _vm.ManualCropPixelHeight = Math.Abs(moveY - fixedY);
                break;
            }
            case DragMode.ResizeE:
            case DragMode.ResizeW:
            {
                // 右辺/左辺: X 軸のみリサイズ。
                var startX = _dragStartRect.X;
                var startRight = _dragStartRect.X + _dragStartRect.W;
                var fixedX = _dragMode is DragMode.ResizeW ? startRight : startX;
                var moveX = Math.Clamp(srcCurrent.Value.SrcX, 0, sw);

                _vm.ManualCropPixelY = _dragStartRect.Y;
                _vm.ManualCropPixelHeight = _dragStartRect.H;
                _vm.ManualCropPixelX = Math.Min(fixedX, moveX);
                _vm.ManualCropPixelWidth = Math.Abs(moveX - fixedX);
                break;
            }
        }
    }
}
