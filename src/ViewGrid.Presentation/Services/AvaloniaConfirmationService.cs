using Avalonia.Controls;
using ViewGrid.Core.Services;
using ViewGrid.Presentation.Views;

namespace ViewGrid.Presentation.Services;

/// <summary>
/// <see cref="ConfirmDialog"/> を用いた <see cref="IConfirmationService"/> 実装。
/// <see cref="AvaloniaFilePickerService"/> と同様、 <see cref="SetOwnerWindow"/> で親ウィンドウを注入してから使用する。
/// </summary>
internal sealed class AvaloniaConfirmationService : IConfirmationService
{
    private Window? _owner;

    public void SetOwnerWindow(Window owner) => _owner = owner;

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct = default)
    {
        if (_owner is null)
            throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");

        return ConfirmDialog.ShowAsync(_owner, title, message, confirmLabel);
    }

    public Task<UnsavedChoice> AskUnsavedChangesAsync(
        string title, string message, string saveLabel, string discardLabel, string cancelLabel,
        CancellationToken ct = default)
    {
        if (_owner is null)
            throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");

        return UnsavedChangesDialog.ShowAsync(_owner, title, message, saveLabel, discardLabel, cancelLabel);
    }
}
