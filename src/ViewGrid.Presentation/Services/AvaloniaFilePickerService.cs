using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ViewGrid.Application.Localization;
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
            Title = LocAccessor.Current["FilePicker_SelectImages_Title"],
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(LocAccessor.Current["FilePicker_Images_TypeName"])
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
            Title = LocAccessor.Current["FilePicker_SavePng_Title"],
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(LocAccessor.Current["FilePicker_PngImage_TypeName"])
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"],
                },
            ],
        };

        var file = await _owner.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickSaveJsonPathAsync(string suggestedFileName, string title, CancellationToken ct = default)
    {
        if (_owner is null)
            throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(LocAccessor.Current["FilePicker_JsonFile_TypeName"])
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"],
                },
            ],
        };

        var file = await _owner.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenJsonPathAsync(string title, CancellationToken ct = default)
    {
        if (_owner is null)
            throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocAccessor.Current["FilePicker_JsonFile_TypeName"])
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"],
                },
                FilePickerFileTypes.All,
            ],
        };

        var files = await _owner.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
