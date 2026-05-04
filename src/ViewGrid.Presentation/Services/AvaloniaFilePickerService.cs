using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ViewGrid.Core.Services;

namespace ViewGrid.Presentation.Services;

/// <summary>
/// Avalonia の <see cref="IStorageProvider"/> を用いた画像ファイル選択ダイアログ。
/// <see cref="SetOwnerWindow"/> で親ウィンドウを注入してから使用する。
/// </summary>
internal sealed class AvaloniaFilePickerService : IFilePickerService
{
    private Window? _owner;

    public void SetOwnerWindow(Window owner) => _owner = owner;

    public async Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default)
    {
        if (_owner is null)
            throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");

        var options = new FilePickerOpenOptions
        {
            Title = "画像を選択",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("画像")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp"],
                },
                FilePickerFileTypes.All,
            ],
        };

        var files = await _owner.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
            return [];

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToArray();
    }

    public async Task<string?> PickSavePngPathAsync(string suggestedFileName, CancellationToken ct = default)
    {
        if (_owner is null)
            throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");

        var options = new FilePickerSaveOptions
        {
            Title = "PNG として保存",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("PNG 画像")
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"],
                },
            ],
        };

        var file = await _owner.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }
}
