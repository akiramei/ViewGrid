using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ViewGrid.Core.Services;
using ViewGrid.Presentation.Views;

namespace ViewGrid.Presentation.Services;

/// <summary>
/// <see cref="ConfirmDialog"/> / <see cref="UnsavedChangesDialog"/> を用いた <see cref="IConfirmationService"/> 実装。
/// <see cref="AvaloniaFilePickerService"/> と同様、 <see cref="SetOwnerWindow"/> で親ウィンドウ (メインウィンドウ) を
/// 注入してから使用する。
/// </summary>
internal sealed class AvaloniaConfirmationService : IConfirmationService
{
    private Window? _owner;

    public void SetOwnerWindow(Window owner) => _owner = owner;

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct = default)
        => ConfirmDialog.ShowAsync(ResolveOwner(), title, message, confirmLabel);

    public Task<UnsavedChoice> AskUnsavedChangesAsync(
        string title, string message, string saveLabel, string discardLabel, string cancelLabel,
        CancellationToken ct = default)
        => UnsavedChangesDialog.ShowAsync(ResolveOwner(), title, message, saveLabel, discardLabel, cancelLabel);

    /// <summary>
    /// 確認ダイアログの親ウィンドウ。 いまアクティブなウィンドウを優先する。 モーダルの設定ダイアログや
    /// ワークスペース切替ダイアログの操作から確認を出すとき、 メインウィンドウを親にすると、 子のモーダルに
    /// ブロックされた親の裏に隠れて操作できなくなるため。 アクティブなウィンドウが無ければメインウィンドウ。
    /// </summary>
    private Window ResolveOwner()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var active = desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible);
            if (active is not null) return active;
        }

        return _owner
            ?? throw new InvalidOperationException("Owner window is not set. Call SetOwnerWindow first.");
    }
}
