using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class PreviewWindow : Window
{
    /// <summary>クリックとドラッグを区別する移動量しきい値（px）。</summary>
    private const double DragThreshold = 3.0;

    /// <summary>
    /// 離散ズーム段階。 ManualCropEditorWindow と同じ系列で、 Ctrl+ホイール / ＋ / − ボタン
    /// で隣の段階へ移動する。 1.0 = 物理ピクセル等倍 (= 出力 PNG と同じサイズ)。
    /// </summary>
    private static readonly double[] ZoomLevels =
        [0.10, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0];

    private byte[]? _bytes;
    private GridWorkspaceViewModel? _workspace;

    /// <summary>現在のズーム倍率。 1.0 = 物理ピクセル等倍。</summary>
    private double _zoom = 1.0;

    /// <summary>フィットモードか (=true) 離散ズームか (=false)。 ZoomLabel の表示、
    /// ToggleButton 同期、 ScrollViewer.SizeChanged 時の自動再フィット判定に使う。</summary>
    private bool _isFitMode = true;

    // ScrollViewer 上のドラッグ・パン状態
    private bool _panning;
    private bool _dragMoved;
    private Point _panPressPoint;
    private Vector _panStartOffset;

    public PreviewWindow()
    {
        InitializeComponent();
        PreviewScrollViewer.PointerPressed += OnPreviewPointerPressed;
        PreviewScrollViewer.PointerMoved += OnPreviewPointerMoved;
        PreviewScrollViewer.PointerReleased += OnPreviewPointerReleased;
        PreviewScrollViewer.PointerCaptureLost += OnPreviewPointerCaptureLost;
        // Wheel は Tunnel 段階で取る。 ScrollViewer 内部の class handler が bubble 途中で
        // Handled=true を立てると、 通常の `+= PointerWheelChanged` (= bubble + handledEventsToo=false)
        // では画像はみ出し時に handler が呼ばれず Ctrl+ホイールがスクロールに飲まれる。
        // Tunnel で先取りして Ctrl のみ消費、 非 Ctrl はスルーして bubble での標準スクロールに任せる。
        PreviewScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPreviewWheel,
            RoutingStrategies.Tunnel);
        // フィットモード中の Window リサイズに追従するため、 ScrollViewer.SizeChanged で
        // 再フィットを実行。 ApplyFitZoom は Image.Width/Height のみ書き換え ScrollViewer 自体の
        // サイズには影響しないので無限ループしない。
        PreviewScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        // 初期表示は常にフィット (Window.Opened で ScrollViewer の Bounds が Layout 完了するのを待つ)
        Opened += OnOpenedFirst;
    }

    /// <summary>
    /// マウス左ボタン押下でパン開始。Capture を取って ScrollViewer 外に出ても
    /// PointerMoved/Released を確実に受ける。スクロールバー上のドラッグは
    /// 標準のスクロールバー動作に任せたいので、その場合はパンしない。
    /// </summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(PreviewScrollViewer).Properties;
        if (!props.IsLeftButtonPressed) return;

        // スクロールバー本体上のクリックは無視（標準の thumb ドラッグ動作を維持）
        if (e.Source is Control c && IsInsideScrollBar(c)) return;

        _panning = true;
        _dragMoved = false;
        _panPressPoint = e.GetPosition(PreviewScrollViewer);
        _panStartOffset = PreviewScrollViewer.Offset;
        e.Pointer.Capture(PreviewScrollViewer);
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning) return;

        var current = e.GetPosition(PreviewScrollViewer);
        var deltaX = current.X - _panPressPoint.X;
        var deltaY = current.Y - _panPressPoint.Y;

        if (!_dragMoved && (Math.Abs(deltaX) >= DragThreshold || Math.Abs(deltaY) >= DragThreshold))
        {
            _dragMoved = true;
            PreviewScrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        if (_dragMoved)
        {
            // 「画像をつかんで動かす」感覚: ドラッグ方向と逆に Offset を動かす
            var newOffset = new Vector(
                _panStartOffset.X - deltaX,
                _panStartOffset.Y - deltaY);
            PreviewScrollViewer.Offset = newOffset;
        }
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        _dragMoved = false;
        PreviewScrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void OnPreviewPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 外部要因（フォーカス喪失等）で Capture が外れたときの保険
        _panning = false;
        _dragMoved = false;
        PreviewScrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// クリックされた Control が ScrollViewer のスクロールバー（thumb / track）配下かを判定する。
    /// スクロールバー操作とパンの両立のため、スクロールバー上のクリックは標準動作に任せる。
    /// </summary>
    private static bool IsInsideScrollBar(Control? control)
    {
        for (var c = control; c is not null; c = c.Parent as Control)
        {
            if (c is Avalonia.Controls.Primitives.ScrollBar)
                return true;
        }
        return false;
    }

    /// <summary>
    /// プレビューに表示する PNG バイト列とワークスペース VM を設定する。
    /// 設定後に <see cref="Window.ShowDialog(Window)"/> で表示する想定。
    /// </summary>
    public void SetSource(byte[] pngBytes, GridWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentNullException.ThrowIfNull(workspace);

        _bytes = pngBytes;
        _workspace = workspace;

        using var stream = new MemoryStream(pngBytes);
        var bitmap = new Bitmap(stream);
        PreviewImage.Source = bitmap;
        InfoText.Text = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} px / {pngBytes.Length:N0} bytes";
    }

    // -------------------- ズーム --------------------

    /// <summary>
    /// 指定された <see cref="_zoom"/> をピクセル単位の Image.Width / Height に反映する。
    /// 物理ピクセル 1:1 を実現するため <see cref="Window.RenderScaling"/> で割って DIP に換算
    /// (1.5x DPI ディスプレイで 1000px の画像 → 667 DIP に表示 → 物理 1000px に戻る)。
    /// </summary>
    private void ApplyZoom()
    {
        if (PreviewImage.Source is not Bitmap bmp) return;
        var scale = this.RenderScaling > 0 ? this.RenderScaling : 1.0;
        PreviewImage.Width = bmp.PixelSize.Width * _zoom / scale;
        PreviewImage.Height = bmp.PixelSize.Height * _zoom / scale;
        ZoomLabel.Text = $"{_zoom * 100:F0}%";
        _isFitMode = false;
        UpdateActiveModeButtons();
    }

    /// <summary>
    /// ScrollViewer の現在のサイズに合わせて画像全体が収まるズーム倍率を計算して適用する。
    /// 100% より大きくはしない (元画像が小さい場合は等倍止まり = DownOnly 相当)、
    /// 写真ボード等で出力が小さいときに不自然に拡大されない。
    /// </summary>
    private void ApplyFitZoom()
    {
        if (PreviewImage.Source is not Bitmap bmp) return;
        _isFitMode = true;
        var sv = PreviewScrollViewer;
        // 内側 Margin 8 + ScrollBar 余裕で約 -16 を引く
        var availW = sv.Bounds.Width - 16;
        var availH = sv.Bounds.Height - 16;
        if (availW <= 0 || availH <= 0)
        {
            // Layout 未完了時は 100% フォールバック (Window.Opened 直後など)
            _zoom = 1.0;
            var s = this.RenderScaling > 0 ? this.RenderScaling : 1.0;
            PreviewImage.Width = bmp.PixelSize.Width / s;
            PreviewImage.Height = bmp.PixelSize.Height / s;
            ZoomLabel.Text = $"フィット ({_zoom * 100:F0}%)";
            UpdateActiveModeButtons();
            return;
        }

        var scale = this.RenderScaling > 0 ? this.RenderScaling : 1.0;
        // 100% 時の DIP サイズ
        var imgDipW = bmp.PixelSize.Width / scale;
        var imgDipH = bmp.PixelSize.Height / scale;
        var fit = Math.Min(availW / imgDipW, availH / imgDipH);
        // DownOnly 相当: 100% を超えないようクランプ (出力が小さい場合に不自然な拡大を避ける)
        if (fit > 1.0) fit = 1.0;
        if (fit <= 0) fit = 1.0;

        _zoom = fit;
        PreviewImage.Width = imgDipW * _zoom;
        PreviewImage.Height = imgDipH * _zoom;
        ZoomLabel.Text = $"フィット ({_zoom * 100:F0}%)";
        UpdateActiveModeButtons();
    }

    /// <summary>
    /// フィット / 100% ToggleButton の IsChecked を現在モードに合わせて更新する。
    /// アクティブモードがどれかを枠の塗りで視覚化し、 ZoomLabel と二重に状態が伝わる。
    /// </summary>
    private void UpdateActiveModeButtons()
    {
        ZoomFitButton.IsChecked = _isFitMode;
        Zoom100Button.IsChecked = !_isFitMode && Math.Abs(_zoom - 1.0) < 1e-6;
    }

    private void OnZoomFitClicked(object? sender, RoutedEventArgs e) => ApplyFitZoom();

    /// <summary>ボタン経由のズームはビューポート中心を基準に拡大・縮小する。</summary>
    private void OnZoom100Clicked(object? sender, RoutedEventArgs e)
        => ZoomAt(ViewportCenter(), 1.0);

    private void OnZoomInClicked(object? sender, RoutedEventArgs e)
        => ZoomAt(ViewportCenter(), NextZoom(_zoom, +1));

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e)
        => ZoomAt(ViewportCenter(), NextZoom(_zoom, -1));

    /// <summary>
    /// Ctrl + マウスホイールでズーム。 Tunnel 段階で先に動くので、 Ctrl 押下時に
    /// Handled=true を立てれば bubble の ScrollViewer.OnPointerWheelChanged が
    /// スクロールしないので動作が「画像の状況」(フィット中 / はみ出し中) で変わらず安定する。
    /// Ctrl 無しは Handled を立てずに bubble へ流すので、 通常のスクロール動作はそのまま維持。
    /// アンカーはマウスカーソル位置 (= カーソル下の画像点が拡大後もカーソル下に残る、
    /// 画像ビューア / 地図アプリ標準の挙動)。
    /// </summary>
    private void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        var direction = e.Delta.Y > 0 ? +1 : -1;
        var anchor = e.GetPosition(PreviewScrollViewer);
        ZoomAt(anchor, NextZoom(_zoom, direction));
        e.Handled = true;
    }

    /// <summary>ScrollViewer ビューポート (= 表示領域) の中心座標。</summary>
    private Point ViewportCenter() => new(
        PreviewScrollViewer.Bounds.Width / 2.0,
        PreviewScrollViewer.Bounds.Height / 2.0);

    /// <summary>
    /// 指定アンカー位置 (ScrollViewer 座標系) を保持しながらズームを変更する。
    /// 「アンカー下にある画像上の点が、 ズーム後も同じ viewport 位置に残る」のが目的。
    /// 画像ビューア / 地図アプリ標準の自然なズーム挙動。
    /// </summary>
    /// <remarks>
    /// 単純に Image.Width/Height を変えるだけだと ScrollViewer の Offset が更新されず、
    /// 画像が左上基準で大きくなる (= 見ていた場所が左上へ吸われる) ように見える。
    /// アンカー保持には次の 4 段階が必要:
    /// <list type="number">
    /// <item>古いレイアウトでアンカー直下の画像座標を [0, 1] 正規化で取得</item>
    /// <item>ズーム適用 (Image.Width/Height 更新)</item>
    /// <item><see cref="Layoutable.UpdateLayout"/> で同期的にレイアウト確定</item>
    /// <item>新レイアウトで同正規化点が viewport 内のどこに移動したかを <see cref="Visual.TranslatePoint"/>
    /// で算出し、 アンカー位置に戻すよう Offset を補正</item>
    /// </list>
    /// <see cref="Visual.TranslatePoint"/> を使うのは <c>HorizontalAlignment=Center</c> による
    /// 「画像が小さいときの中央寄せ」も自動で吸収できるため (画像位置を手計算せず済む)。
    /// </remarks>
    private void ZoomAt(Point anchor, double newZoom)
    {
        if (PreviewImage.Source is not Bitmap bmp) return;

        var scale = this.RenderScaling > 0 ? this.RenderScaling : 1.0;
        var oldDipW = bmp.PixelSize.Width * _zoom / scale;
        var oldDipH = bmp.PixelSize.Height * _zoom / scale;

        // 1. 古いレイアウトでアンカー直下の正規化画像座標を取得 ([0, 1] にクランプ)
        double nx = 0.5, ny = 0.5;
        var oldOrigin = PreviewImage.TranslatePoint(new Point(0, 0), PreviewScrollViewer);
        if (oldOrigin is { } o1 && oldDipW > 0 && oldDipH > 0)
        {
            nx = Math.Clamp((anchor.X - o1.X) / oldDipW, 0.0, 1.0);
            ny = Math.Clamp((anchor.Y - o1.Y) / oldDipH, 0.0, 1.0);
        }

        // 2. ズーム適用 (Image.Width/Height + ToggleButton 同期)
        _zoom = newZoom;
        ApplyZoom();

        // 3. レイアウト同期: Image.Bounds と TranslatePoint が新サイズを反映するため
        UpdateLayout();

        // 4. 新レイアウトでの「同じ正規化点の viewport 位置」をアンカーへ寄せる Offset 補正
        var newDipW = bmp.PixelSize.Width * _zoom / scale;
        var newDipH = bmp.PixelSize.Height * _zoom / scale;
        var newOrigin = PreviewImage.TranslatePoint(new Point(0, 0), PreviewScrollViewer);
        if (newOrigin is not { } o2) return;

        var pointAfter = new Point(o2.X + nx * newDipW, o2.Y + ny * newDipH);
        var deltaX = pointAfter.X - anchor.X;
        var deltaY = pointAfter.Y - anchor.Y;

        // Offset 範囲外は ScrollViewer 側で自動クランプ (画像が viewport より小さい場合は 0 に戻る)
        PreviewScrollViewer.Offset = new Vector(
            PreviewScrollViewer.Offset.X + deltaX,
            PreviewScrollViewer.Offset.Y + deltaY);
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

    /// <summary>
    /// ウィンドウ初回表示時に常にフィットを適用。 「プレビューを開いた瞬間にまず全体構図を
    /// 確認 → 細部は 100% / 拡大に切替」という一般的な体験を優先し、 前回ズーム記憶は採用しない。
    /// </summary>
    private void OnOpenedFirst(object? sender, EventArgs e)
    {
        Opened -= OnOpenedFirst;
        ApplyFitZoom();
    }

    /// <summary>
    /// ScrollViewer のサイズが変わったとき (= Window リサイズ) に、 フィットモード中なら
    /// 自動でフィットを再計算。 「フィット」というモード名と挙動を一致させる。
    /// 100% / 離散ズーム時は何もしない (ユーザーが選んだ倍率を保持)。
    /// </summary>
    private void OnScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isFitMode) ApplyFitZoom();
    }

    // -------------------- 保存 / 閉じる --------------------

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_bytes is null || _workspace is null) return;
        SaveButton.IsEnabled = false;
        try
        {
            var saved = await _workspace.SavePngBytesAsync(_bytes);
            if (saved) Close();
        }
        catch (Exception)
        {
            // async void イベントハンドラの例外が SynchronizationContext へ漏れると
            // プロセスクラッシュを招くため最終防御。 IOException / UnauthorizedAccessException
            // は SavePngBytesAsync 内部で StatusMessage に逃げているので、 ここで掴むのは
            // 予期せぬ例外 (NullReference / OOM / 取消等) のみのはず。
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
