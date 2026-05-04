using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// ManualCrop（任意矩形トリミング）の詳細編集ダイアログ。元画像を ScrollViewer 上で
/// ズーム表示し、8 ハンドル + マット表示 + 自動スクロール付きで矩形を編集する。
/// 親 (CopyPropertiesView) から <see cref="Initialize"/> で入力値を渡し、
/// <c>ShowDialog</c> 後に <see cref="GetResult"/> で OK 時のみ
/// 編集後の値を取得する（キャンセル時は <c>null</c>）。
/// </summary>
public partial class ManualCropEditorWindow : Window
{
    private enum DragMode
    {
        None,
        CreateNew,
        Move,
        ResizeNW, ResizeNE, ResizeSW, ResizeSE,
        ResizeN, ResizeS, ResizeE, ResizeW,
    }

    private const double HandleSize = 14;
    private const double HandleHitSlack = 4;

    /// <summary>自動スクロールが発動する ScrollViewer 端からの余白。</summary>
    private const double AutoScrollMargin = 30;

    /// <summary>1 PointerMoved あたりの自動スクロール量（px）。</summary>
    private const double AutoScrollSpeed = 12;

    private static readonly double[] ZoomLevels = [0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0];

    // 入力
    private int _sourceWidth;
    private int _sourceHeight;

    // 編集中の矩形（元画像ピクセル）
    private double _x;
    private double _y;
    private double _w;
    private double _h;

    private double _zoom = 1.0;

    // ドラッグ状態
    private bool _isDragging;
    private DragMode _dragMode;
    private Point _dragStartPoint;
    private (double X, double Y, double W, double H) _dragStartRect;

    // 入力同期抑止
    private bool _suppressNumericSync;

    // 結果
    private bool _committed;

    public ManualCropEditorWindow()
    {
        InitializeComponent();
        EditorImage.PropertyChanged += (_, e) =>
        {
            // Image の Bounds 変化（zoom 適用後の再レイアウト）でオーバーレイ再描画。
            // CopyPropertiesView と同パターンで LayoutUpdated は使わない（再帰防止）。
            if (e.Property == BoundsProperty) UpdateOverlay();
        };
    }

    /// <summary>
    /// ダイアログを初期化する。<c>ShowDialog</c> の前に呼ぶこと。
    /// 入力ピクセル値が <c>0</c> なら未確定スタート（ユーザーがドラッグして初期矩形作成）。
    /// </summary>
    public void Initialize(string imagePath, int sourceWidth, int sourceHeight,
        double initialX, double initialY, double initialWidth, double initialHeight)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("画像ファイルが見つかりません", imagePath);

        _sourceWidth = sourceWidth > 0 ? sourceWidth : 1;
        _sourceHeight = sourceHeight > 0 ? sourceHeight : 1;
        _x = Math.Clamp(initialX, 0, _sourceWidth);
        _y = Math.Clamp(initialY, 0, _sourceHeight);
        _w = Math.Clamp(initialWidth, 0, _sourceWidth - _x);
        _h = Math.Clamp(initialHeight, 0, _sourceHeight - _y);

        // 画像ロード（using しない: Bitmap が解放されると Image.Source も無効化される）
        var stream = File.OpenRead(imagePath);
        try
        {
            var bitmap = new Bitmap(stream);
            EditorImage.Source = bitmap;
        }
        finally
        {
            stream.Dispose();
        }

        // 数値入力 Maximum 設定
        XInput.Maximum = _sourceWidth;
        YInput.Maximum = _sourceHeight;
        WInput.Maximum = _sourceWidth;
        HInput.Maximum = _sourceHeight;

        SyncNumericFromState();
        // 初期表示はフィット（Window が Layout 完了するまで待つため Loaded で）
        Opened += OnOpenedFitFirst;
    }

    private void OnOpenedFitFirst(object? sender, EventArgs e)
    {
        Opened -= OnOpenedFitFirst;
        ApplyFitZoom();
    }

    /// <summary>OK が押された場合のみ編集後の矩形を返す。キャンセル時は <c>null</c>。</summary>
    public (double X, double Y, double W, double H)? GetResult()
        => _committed ? (_x, _y, _w, _h) : null;

    // -------------------- ズーム --------------------

    private void ApplyZoom()
    {
        EditorImage.Width = _sourceWidth * _zoom;
        EditorImage.Height = _sourceHeight * _zoom;
        ZoomLabel.Text = $"{_zoom * 100:F0}%";
        UpdateOverlay();
    }

    private void ApplyFitZoom()
    {
        var sv = EditorScrollViewer;
        var availW = sv.Bounds.Width - 4;
        var availH = sv.Bounds.Height - 4;
        if (availW <= 0 || availH <= 0 || _sourceWidth <= 0 || _sourceHeight <= 0)
        {
            _zoom = 1.0;
        }
        else
        {
            _zoom = Math.Min(availW / _sourceWidth, availH / _sourceHeight);
            if (_zoom <= 0) _zoom = 1.0;
        }
        ApplyZoom();
    }

    private void OnZoomFitClicked(object? sender, RoutedEventArgs e) => ApplyFitZoom();

    private void OnZoom100Clicked(object? sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        ApplyZoom();
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e)
    {
        _zoom = NextZoom(_zoom, +1);
        ApplyZoom();
    }

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        _zoom = NextZoom(_zoom, -1);
        ApplyZoom();
    }

    /// <summary>離散ズーム段階の中で direction (+1/-1) 方向の隣を返す。</summary>
    private static double NextZoom(double current, int direction)
    {
        if (direction > 0)
        {
            for (var i = 0; i < ZoomLevels.Length; i++)
            {
                if (ZoomLevels[i] > current + 1e-6) return ZoomLevels[i];
            }
            return ZoomLevels[^1];
        }
        else
        {
            for (var i = ZoomLevels.Length - 1; i >= 0; i--)
            {
                if (ZoomLevels[i] < current - 1e-6) return ZoomLevels[i];
            }
            return ZoomLevels[0];
        }
    }

    // -------------------- 数値入力同期 --------------------

    private void SyncNumericFromState()
    {
        _suppressNumericSync = true;
        try
        {
            XInput.Value = (decimal)_x;
            YInput.Value = (decimal)_y;
            WInput.Value = (decimal)_w;
            HInput.Value = (decimal)_h;
        }
        finally
        {
            _suppressNumericSync = false;
        }
    }

    private void OnNumericValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressNumericSync) return;
        _x = Math.Clamp((double)(XInput.Value ?? 0m), 0, _sourceWidth);
        _y = Math.Clamp((double)(YInput.Value ?? 0m), 0, _sourceHeight);
        _w = Math.Clamp((double)(WInput.Value ?? 0m), 0, _sourceWidth - _x);
        _h = Math.Clamp((double)(HInput.Value ?? 0m), 0, _sourceHeight - _y);
        UpdateOverlay();
    }

    // -------------------- 座標変換 --------------------

    /// <summary>元画像ピクセル → オーバーレイ座標（zoom 倍率を掛けるだけ、padding なし）。</summary>
    private (double X, double Y, double W, double H) PixelToOverlayRect()
        => (_x * _zoom, _y * _zoom, _w * _zoom, _h * _zoom);

    private (double SrcX, double SrcY) OverlayToPixel(Point p)
    {
        if (_zoom <= 0) return (0, 0);
        return (p.X / _zoom, p.Y / _zoom);
    }

    // -------------------- オーバーレイ描画 --------------------

    /// <summary>
    /// マット 4 領域 + 矩形枠 + 8 ハンドルを再描画する。
    /// 矩形未確定（W=0 or H=0）時はオーバーレイは空（マットも出さない）。
    /// </summary>
    private void UpdateOverlay()
    {
        if (EditorOverlay is null) return;
        EditorOverlay.Children.Clear();

        if (_w <= 0 || _h <= 0) return;

        var displayW = _sourceWidth * _zoom;
        var displayH = _sourceHeight * _zoom;
        var r = PixelToOverlayRect();

        // マット 4 領域（矩形外側を半透明黒で覆う）
        var mattBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        AddMatt(0, 0, displayW, r.Y, mattBrush);                          // 上
        AddMatt(0, r.Y + r.H, displayW, displayH - (r.Y + r.H), mattBrush); // 下
        AddMatt(0, r.Y, r.X, r.H, mattBrush);                             // 左
        AddMatt(r.X + r.W, r.Y, displayW - (r.X + r.W), r.H, mattBrush);  // 右

        // 矩形枠
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
        EditorOverlay.Children.Add(border);

        // 4 隅 + 4 辺中央 = 8 ハンドル
        AddHandle(r.X, r.Y);
        AddHandle(r.X + r.W, r.Y);
        AddHandle(r.X, r.Y + r.H);
        AddHandle(r.X + r.W, r.Y + r.H);
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
            Width = w, Height = h, Fill = brush, IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        EditorOverlay.Children.Add(rect);
    }

    private void AddHandle(double cx, double cy)
    {
        var h = new Rectangle
        {
            Width = HandleSize, Height = HandleSize,
            Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            Stroke = Brushes.White, StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(h, cx - HandleSize / 2);
        Canvas.SetTop(h, cy - HandleSize / 2);
        EditorOverlay.Children.Add(h);
    }

    // -------------------- ヒットテスト --------------------

    private static DragMode HitTestHandle(Point p, (double X, double Y, double W, double H) r)
    {
        var slack = HandleSize / 2 + HandleHitSlack;
        if (Distance(p, new Point(r.X, r.Y)) <= slack) return DragMode.ResizeNW;
        if (Distance(p, new Point(r.X + r.W, r.Y)) <= slack) return DragMode.ResizeNE;
        if (Distance(p, new Point(r.X, r.Y + r.H)) <= slack) return DragMode.ResizeSW;
        if (Distance(p, new Point(r.X + r.W, r.Y + r.H)) <= slack) return DragMode.ResizeSE;
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

    // -------------------- ポインタイベント --------------------

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(EditorOverlay).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(EditorOverlay);

        if (_w > 0 && _h > 0)
        {
            var r = PixelToOverlayRect();
            var hit = HitTestHandle(pos, r);
            if (hit != DragMode.None) _dragMode = hit;
            else if (IsInsideRect(pos, r)) _dragMode = DragMode.Move;
            else _dragMode = DragMode.CreateNew;
        }
        else
        {
            _dragMode = DragMode.CreateNew;
        }

        _isDragging = true;
        _dragStartPoint = pos;
        _dragStartRect = (_x, _y, _w, _h);
        e.Pointer.Capture(EditorOverlay);
        e.Handled = true;
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        var posOverlay = e.GetPosition(EditorOverlay);
        ApplyDragUpdate(posOverlay);

        // ドラッグ中の自動スクロール（ScrollViewer 内座標で端付近をチェック）
        var posSv = e.GetPosition(EditorScrollViewer);
        AutoScrollIfNeeded(posSv);
    }

    private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// PointerMoved 中の各モードに応じた _x/_y/_w/_h 更新。CopyPropertiesView と同じロジック。
    /// オーバーレイ座標 → ピクセル座標は zoom で逆換算するだけ（padding なし）。
    /// </summary>
    private void ApplyDragUpdate(Point pos)
    {
        var (curX, curY) = OverlayToPixel(pos);
        var (startX, startY) = OverlayToPixel(_dragStartPoint);

        switch (_dragMode)
        {
            case DragMode.CreateNew:
            {
                var x1 = Math.Clamp(startX, 0, _sourceWidth);
                var y1 = Math.Clamp(startY, 0, _sourceHeight);
                var x2 = Math.Clamp(curX, 0, _sourceWidth);
                var y2 = Math.Clamp(curY, 0, _sourceHeight);
                _x = Math.Min(x1, x2);
                _y = Math.Min(y1, y2);
                _w = Math.Abs(x2 - x1);
                _h = Math.Abs(y2 - y1);
                break;
            }
            case DragMode.Move:
            {
                var dx = curX - startX;
                var dy = curY - startY;
                _x = Math.Clamp(_dragStartRect.X + dx, 0, _sourceWidth - _dragStartRect.W);
                _y = Math.Clamp(_dragStartRect.Y + dy, 0, _sourceHeight - _dragStartRect.H);
                break;
            }
            case DragMode.ResizeNW:
            case DragMode.ResizeNE:
            case DragMode.ResizeSW:
            case DragMode.ResizeSE:
            {
                var sx = _dragStartRect.X;
                var sy = _dragStartRect.Y;
                var sr = _dragStartRect.X + _dragStartRect.W;
                var sb = _dragStartRect.Y + _dragStartRect.H;
                var fixedX = (_dragMode is DragMode.ResizeNE or DragMode.ResizeSE) ? sx : sr;
                var fixedY = (_dragMode is DragMode.ResizeSW or DragMode.ResizeSE) ? sy : sb;
                var moveX = Math.Clamp(curX, 0, _sourceWidth);
                var moveY = Math.Clamp(curY, 0, _sourceHeight);
                _x = Math.Min(fixedX, moveX);
                _y = Math.Min(fixedY, moveY);
                _w = Math.Abs(moveX - fixedX);
                _h = Math.Abs(moveY - fixedY);
                break;
            }
            case DragMode.ResizeN:
            case DragMode.ResizeS:
            {
                var sy = _dragStartRect.Y;
                var sb = _dragStartRect.Y + _dragStartRect.H;
                var fixedY = _dragMode is DragMode.ResizeN ? sb : sy;
                var moveY = Math.Clamp(curY, 0, _sourceHeight);
                _x = _dragStartRect.X;
                _w = _dragStartRect.W;
                _y = Math.Min(fixedY, moveY);
                _h = Math.Abs(moveY - fixedY);
                break;
            }
            case DragMode.ResizeE:
            case DragMode.ResizeW:
            {
                var sx = _dragStartRect.X;
                var sr = _dragStartRect.X + _dragStartRect.W;
                var fixedX = _dragMode is DragMode.ResizeW ? sr : sx;
                var moveX = Math.Clamp(curX, 0, _sourceWidth);
                _y = _dragStartRect.Y;
                _h = _dragStartRect.H;
                _x = Math.Min(fixedX, moveX);
                _w = Math.Abs(moveX - fixedX);
                break;
            }
        }

        SyncNumericFromState();
        UpdateOverlay();
    }

    /// <summary>
    /// ScrollViewer 内のポインタ位置 (sv 座標) が端から <see cref="AutoScrollMargin"/> 以内なら
    /// その方向に <see cref="AutoScrollSpeed"/> px だけ Offset を移動する。PointerMoved 毎に
    /// 1 回呼ばれるので、ポインタを端付近で保持し続けるとスクロールが連続する。
    /// </summary>
    private void AutoScrollIfNeeded(Point posSv)
    {
        var sv = EditorScrollViewer;
        var bw = sv.Bounds.Width;
        var bh = sv.Bounds.Height;
        if (bw <= 0 || bh <= 0) return;

        var off = sv.Offset;
        var newX = off.X;
        var newY = off.Y;
        if (posSv.X < AutoScrollMargin) newX -= AutoScrollSpeed;
        else if (posSv.X > bw - AutoScrollMargin) newX += AutoScrollSpeed;
        if (posSv.Y < AutoScrollMargin) newY -= AutoScrollSpeed;
        else if (posSv.Y > bh - AutoScrollMargin) newY += AutoScrollSpeed;

        if (newX == off.X && newY == off.Y) return;

        // Extent / Viewport で max を計算してクランプ
        var maxX = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        sv.Offset = new Vector(Math.Clamp(newX, 0, maxX), Math.Clamp(newY, 0, maxY));
    }

    // -------------------- OK / Cancel --------------------

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        _committed = true;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        _committed = false;
        Close();
    }
}
