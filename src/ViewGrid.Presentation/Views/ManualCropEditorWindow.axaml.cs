using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// 元画像座標系のピクセル矩形を 8 ハンドル + マット表示 + 自動スクロール付きで編集する
/// 汎用ダイアログ。 元は ManualCrop 専用だったが、 入出力 (画像パス + 初期矩形 → 編集後矩形)
/// が完全に汎用なので、 ProtectedRegion (保護領域) の矩形編集にも再利用する。
/// 呼び出し側は <see cref="Window.Title"/> を上書きして用途を区別する。
/// </summary>
/// <remarks>
/// 親 (CopyPropertiesView) から <see cref="Initialize"/> で入力値を渡し、 <c>ShowDialog</c>
/// 後に <see cref="GetResult"/> で OK 時のみ編集後の値を取得する (キャンセル時は <c>null</c>)。
/// クラス名は歴史的理由で残しているが、 機能としては rect editor。
/// </remarks>
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

    /// <summary>パンのクリック / ドラッグ区別しきい値（px、 PreviewWindow と同値）。</summary>
    private const double PanDragThreshold = 3.0;

    private static readonly double[] ZoomLevels = [0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 16.0];

    // 入力
    private int _sourceWidth;
    private int _sourceHeight;

    // 編集中の矩形（元画像ピクセル整数）
    private int _x;
    private int _y;
    private int _w;
    private int _h;

    private double _zoom = 1.0;

    // ドラッグ状態
    private bool _isDragging;
    private DragMode _dragMode;
    private Point _dragStartPoint;
    private (int X, int Y, int W, int H) _dragStartRect;

    // パン状態（中ボタン or Space+左ボタン）
    private bool _panActive;
    private bool _panMoved;
    private Point _panPressPoint;
    private Vector _panStartOffset;

    // Space 押下中フラグ（Space + 左ドラッグでパンするため）
    private bool _spaceHeld;

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

        // Space + 左ドラッグでパンするための Window レベル KeyDown / KeyUp + Deactivated。
        // KeyDown は Tunnel 段階で取り、 NumericUpDown 配下のフォーカス時は早期リターンする。
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        Deactivated += OnWindowDeactivated;
    }

    /// <summary>
    /// ダイアログを初期化する。<c>ShowDialog</c> の前に呼ぶこと。
    /// 入力ピクセル値が <c>0</c> なら未確定スタート（ユーザーがドラッグして初期矩形作成）。
    /// </summary>
    public void Initialize(string imagePath, int sourceWidth, int sourceHeight,
        int initialX, int initialY, int initialWidth, int initialHeight)
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
    public (int X, int Y, int W, int H)? GetResult()
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
        // NumericUpDown.Value は decimal? なので int に丸めて取り込む (FormatString="0" で表示も整数)
        _x = Math.Clamp((int)(XInput.Value ?? 0m), 0, _sourceWidth);
        _y = Math.Clamp((int)(YInput.Value ?? 0m), 0, _sourceHeight);
        _w = Math.Clamp((int)(WInput.Value ?? 0m), 0, _sourceWidth - _x);
        _h = Math.Clamp((int)(HInput.Value ?? 0m), 0, _sourceHeight - _y);
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
        var props = e.GetCurrentPoint(EditorOverlay).Properties;

        // 中ボタンドラッグはパン（左ボタンの矩形操作と完全分離）
        if (props.IsMiddleButtonPressed)
        {
            StartPan(e);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        // Space 押下中の左ドラッグはパン (HitTestHandle / IsInsideRect を完全スキップ)
        if (_spaceHeld)
        {
            StartPan(e);
            e.Handled = true;
            return;
        }

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
        if (_panActive)
        {
            UpdatePan(e);
            return;
        }
        if (!_isDragging) return;
        var posOverlay = e.GetPosition(EditorOverlay);
        ApplyDragUpdate(posOverlay);

        // ドラッグ中の自動スクロール（ScrollViewer 内座標で端付近をチェック）
        var posSv = e.GetPosition(EditorScrollViewer);
        AutoScrollIfNeeded(posSv);
    }

    private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panActive)
        {
            EndPan(e);
            return;
        }
        if (!_isDragging) return;
        _isDragging = false;
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // -------------------- パン --------------------

    /// <summary>
    /// 中ボタン or Space+左ボタンでパン開始。 Capture を取って Overlay 外でも
    /// PointerMoved / Released を確実に受ける。 PreviewWindow の左ボタンパンと
    /// 同じ「画像をつかんで動かす」 (ドラッグと逆方向に Offset) 挙動。
    /// </summary>
    private void StartPan(PointerPressedEventArgs e)
    {
        _panActive = true;
        _panMoved = false;
        _panPressPoint = e.GetPosition(EditorScrollViewer);
        _panStartOffset = EditorScrollViewer.Offset;
        e.Pointer.Capture(EditorOverlay);
    }

    private void UpdatePan(PointerEventArgs e)
    {
        var current = e.GetPosition(EditorScrollViewer);
        var deltaX = current.X - _panPressPoint.X;
        var deltaY = current.Y - _panPressPoint.Y;

        if (!_panMoved && (Math.Abs(deltaX) >= PanDragThreshold || Math.Abs(deltaY) >= PanDragThreshold))
        {
            _panMoved = true;
            EditorOverlay.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        if (_panMoved)
        {
            // ScrollViewer.Offset は Extent / Viewport により自動クランプされる
            EditorScrollViewer.Offset = new Vector(
                _panStartOffset.X - deltaX,
                _panStartOffset.Y - deltaY);
        }
    }

    private void EndPan(PointerReleasedEventArgs e)
    {
        _panActive = false;
        _panMoved = false;
        // Space 押下中なら SizeAll 維持 (Space を離した時点で Cross に戻す)
        EditorOverlay.Cursor = new Cursor(_spaceHeld ? StandardCursorType.SizeAll : StandardCursorType.Cross);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// 外部要因 (フォーカス喪失等) で Capture が外れたときのリセット保険。
    /// 矩形ドラッグ / パンの両方を同時にリセットして整合性を保つ。
    /// </summary>
    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _panActive = false;
        _panMoved = false;
        _isDragging = false;
        _dragMode = DragMode.None;
        EditorOverlay.Cursor = new Cursor(_spaceHeld ? StandardCursorType.SizeAll : StandardCursorType.Cross);
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
                // ドラッグ起点 / 現在点はオーバーレイ座標 (double) なので、 ピクセル境界で
                // Math.Round → Math.Clamp の順で int に丸める (整数スナップ)
                var x1 = (int)Math.Clamp(Math.Round(startX), 0, _sourceWidth);
                var y1 = (int)Math.Clamp(Math.Round(startY), 0, _sourceHeight);
                var x2 = (int)Math.Clamp(Math.Round(curX), 0, _sourceWidth);
                var y2 = (int)Math.Clamp(Math.Round(curY), 0, _sourceHeight);
                _x = Math.Min(x1, x2);
                _y = Math.Min(y1, y2);
                _w = Math.Abs(x2 - x1);
                _h = Math.Abs(y2 - y1);
                break;
            }
            case DragMode.Move:
            {
                // dx/dy は double のまま計算し、 最終代入で int に丸める
                var dx = curX - startX;
                var dy = curY - startY;
                _x = (int)Math.Clamp(Math.Round(_dragStartRect.X + dx), 0, _sourceWidth - _dragStartRect.W);
                _y = (int)Math.Clamp(Math.Round(_dragStartRect.Y + dy), 0, _sourceHeight - _dragStartRect.H);
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
                var moveX = (int)Math.Clamp(Math.Round(curX), 0, _sourceWidth);
                var moveY = (int)Math.Clamp(Math.Round(curY), 0, _sourceHeight);
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
                var moveY = (int)Math.Clamp(Math.Round(curY), 0, _sourceHeight);
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
                var moveX = (int)Math.Clamp(Math.Round(curX), 0, _sourceWidth);
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

        // AutoScroll 量 (整数定数 AutoScrollSpeed) を加減した結果の double 値を、
        // double 演算誤差を許容する形で比較する (実用上は「動かないケース」を弾くだけの早期 return)。
        if (Math.Abs(newX - off.X) < 1e-6 && Math.Abs(newY - off.Y) < 1e-6) return;

        // Extent / Viewport で max を計算してクランプ
        var maxX = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        sv.Offset = new Vector(Math.Clamp(newX, 0, maxX), Math.Clamp(newY, 0, maxY));
    }

    // -------------------- キーハンドラ (Space + 矢印キー) --------------------

    /// <summary>
    /// Space 押下で「パン待機」状態へ + 矢印キーで矩形を 1px (Shift で 10px) 移動。
    /// 矩形操作中 (`_isDragging`) / パン中は無視して現操作を優先。
    /// 下部の NumericUpDown にフォーカスがある場合も干渉を避けるため早期リターン。
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // 矩形操作中 / パン中は何のキー入力も受け付けない (現操作優先)
        if (_isDragging || _panActive) return;
        // NumericUpDown 配下フォーカス時は標準動作に任せる (Space は数値入力、 矢印は値増減)
        if (IsInsideNumericInput(e.Source as Control)) return;

        if (e.Key == Key.Space)
        {
            if (!_spaceHeld)
            {
                _spaceHeld = true;
                EditorOverlay.Cursor = new Cursor(StandardCursorType.SizeAll);
            }
            // Space を消費して伝播を防ぐ
            e.Handled = true;
            return;
        }

        // 矢印キーは矩形が確定しているときのみ反応
        if (_w == 0 || _h == 0) return;
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1;
        var dx = 0;
        var dy = 0;
        switch (e.Key)
        {
            case Key.Up: dy = -step; break;
            case Key.Down: dy = step; break;
            case Key.Left: dx = -step; break;
            case Key.Right: dx = step; break;
            default: return;
        }
        _x = Math.Clamp(_x + dx, 0, _sourceWidth - _w);
        _y = Math.Clamp(_y + dy, 0, _sourceHeight - _h);
        SyncNumericFromState();
        UpdateOverlay();
        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (!_spaceHeld) return;
        _spaceHeld = false;
        // パン継続中なら EndPan が解放後に Cross に戻すので、 ここではパン非アクティブ時のみ戻す
        if (!_panActive) EditorOverlay.Cursor = new Cursor(StandardCursorType.Cross);
        e.Handled = true;
    }

    /// <summary>
    /// Window 非アクティブ化で Space 状態をリセット (Alt+Tab 中の押下で取り残しを防ぐ)。
    /// パン中の Capture は別途 PointerCaptureLost が拾うのでここでは触らない。
    /// </summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_spaceHeld) return;
        _spaceHeld = false;
        if (!_panActive) EditorOverlay.Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>
    /// 指定コントロール (= KeyDown の Source) が下部の数値入力 (`NumericUpDown`) 配下かを判定する。
    /// 数値入力にフォーカスがあるときの Space は数値テキスト編集に任せる。
    /// </summary>
    private static bool IsInsideNumericInput(Control? control)
    {
        for (var c = control; c is not null; c = c.Parent as Control)
        {
            if (c is NumericUpDown) return true;
        }
        return false;
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
