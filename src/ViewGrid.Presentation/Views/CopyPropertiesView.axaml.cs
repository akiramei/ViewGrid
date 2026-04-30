using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class CopyPropertiesView : UserControl
{
    public CopyPropertiesView()
    {
        InitializeComponent();
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

        // Stretch.Uniform: bitmap がコントロール内にアスペクト維持でフィット。
        // 表示倍率 = min(imgW/bmpW, imgH/bmpH)、余白は両端に均等に発生。
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
            // サムネ座標 + サムネ実寸を渡す。VM 側で原画像座標に等比換算して原画像から色採取する。
            await vm.PickColorFromThumbnailAsync(px, py, bmpW, bmpH);
        }
        catch
        {
            // ユーザー操作起点の例外は握りつぶす（StatusMessage 等で表示される）
        }
    }
}
