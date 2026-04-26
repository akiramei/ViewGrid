using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class PreviewWindow : Window
{
    private byte[]? _bytes;
    private GridWorkspaceViewModel? _workspace;

    public PreviewWindow()
    {
        InitializeComponent();
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
