using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class PreviewWindow : Window
{
    /// <summary>クリックとドラッグを区別する移動量しきい値（px）。</summary>
    private const double DragThreshold = 3.0;

    private byte[]? _bytes;
    private GridWorkspaceViewModel? _workspace;

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
        InfoText.Text = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} / {pngBytes.Length:N0} bytes";
    }

    private async void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_bytes is null || _workspace is null) return;
        SaveButton.IsEnabled = false;
        try
        {
            var saved = await _workspace.SavePngBytesAsync(_bytes);
            if (saved) Close();
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
