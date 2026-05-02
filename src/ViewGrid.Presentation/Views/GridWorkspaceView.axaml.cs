using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Presentation.Views;

public partial class GridWorkspaceView : UserControl
{
    private const double DragThreshold = 2.0;

    private Point? _pressOrigin;
    private CopyCandidateViewModel? _pressItem;
    private PointerPressedEventArgs? _pressEvent;

    public GridWorkspaceView()
    {
        InitializeComponent();
        // TreeView 配下のアイテムから発生するイベントをトンネルでフックする
        CandidateTree.AddHandler(PointerPressedEvent, OnCandidatePointerPressed, RoutingStrategies.Tunnel);
        CandidateTree.AddHandler(PointerMovedEvent, OnCandidatePointerMoved, RoutingStrategies.Tunnel);
        CandidateTree.AddHandler(PointerReleasedEvent, OnCandidatePointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnCandidatePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CandidateTree).Properties.IsLeftButtonPressed)
            return;

        var item = FindCandidateItem(e.Source);
        if (item is null)
            return;

        // インラインリネーム編集中はサムネ D&D を無効化（TextBox 内のテキスト選択を
        // 誤ってドラッグ操作と解釈しないため）
        if (item.IsEditing) return;

        _pressOrigin = e.GetPosition(CandidateTree);
        _pressItem = item;
        _pressEvent = e;
    }

    private async void OnCandidatePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressOrigin is null || _pressItem is null || _pressEvent is null)
            return;

        var current = e.GetPosition(CandidateTree);
        var dx = Math.Abs(current.X - _pressOrigin.Value.X);
        var dy = Math.Abs(current.Y - _pressOrigin.Value.Y);
        if (dx < DragThreshold && dy < DragThreshold)
            return;

        var item = _pressItem;
        var trigger = _pressEvent;
        ResetPressState();

        var transfer = new DataTransfer();
        // 候補ドラッグはサムネ全体が source なので offset (0,0) 固定。
        transfer.Add(DataTransferItem.CreateText($"copy:{item.CopyId}:0,0"));

        try
        {
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Copy);
        }
        catch
        {
            // D&D 中の例外はユーザー操作起点なので握りつぶす
        }
    }

    private void OnCandidatePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ResetPressState();
    }

    private void ResetPressState()
    {
        _pressOrigin = null;
        _pressItem = null;
        _pressEvent = null;
    }

    private async void OnPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GridWorkspaceViewModel vm)
            return;

        PreviewButton.IsEnabled = false;
        try
        {
            var bytes = await vm.RequestPreviewAsync();
            if (bytes is null || bytes.Length == 0)
                return;

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null)
                return;

            var preview = new PreviewWindow();
            preview.SetSource(bytes, vm);
            await preview.ShowDialog(owner);
        }
        finally
        {
            PreviewButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// TreeView 内の任意 Visual から、最も内側の TreeViewItem を遡って
    /// その DataContext が <see cref="CopyCandidateViewModel"/> のときだけ返す。
    /// グループヘッダ（<see cref="CandidateGroupViewModel"/>）配下では null を返し、
    /// D&D / インラインリネームの起動を抑止する。
    /// </summary>
    private static CopyCandidateViewModel? FindCandidateItem(object? source)
    {
        if (source is not Visual visual)
            return null;

        var current = visual;
        while (current is not null)
        {
            if (current is TreeViewItem tvi && tvi.DataContext is CopyCandidateViewModel vm)
                return vm;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ─── 配置ファースト UI 第 2 段階 (Stage 2): バリアント候補のインラインリネーム ───

    /// <summary>
    /// ダブルクリックでインラインリネーム編集を開始する（CopyListView と同パターン）。
    /// グループヘッダ上のダブルクリックは展開/折り畳みの標準動作に任せて no-op。
    /// </summary>
    private void OnCandidateListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not GridWorkspaceViewModel vm) return;
        if (e.Source is not Control src) return;

        var item = src as TreeViewItem ?? src.FindAncestorOfType<TreeViewItem>();
        if (item?.DataContext is not CopyCandidateViewModel candidate) return;
        if (candidate.IsEditing) return;

        vm.BeginEditCandidate(candidate);
        e.Handled = true;
    }

    /// <summary>
    /// F2 キーで選択中の候補をリネーム編集モードに切り替える。
    /// </summary>
    private void OnCandidateListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2) return;
        if (DataContext is not GridWorkspaceViewModel vm) return;
        if (vm.SelectedCandidate is not { } candidate) return;

        vm.BeginEditCandidate(candidate);
        e.Handled = true;
    }

    /// <summary>
    /// 編集 TextBox 上の Enter で確定 / Esc でキャンセル。
    /// </summary>
    private async void OnEditingCandidateKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not CopyCandidateViewModel candidate) return;
        if (DataContext is not GridWorkspaceViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await vm.CommitEditCandidateAsync(candidate);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEditCandidate(candidate);
        }
    }

    /// <summary>
    /// フォーカス喪失で確定（一般的なインラインリネーム UX）。Esc キャンセルが
    /// 既に IsEditing=false にしている場合は no-op。
    /// </summary>
    private async void OnEditingCandidateLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not CopyCandidateViewModel candidate) return;
        if (DataContext is not GridWorkspaceViewModel vm) return;
        if (!candidate.IsEditing) return; // 既にキャンセル / 確定済み

        await vm.CommitEditCandidateAsync(candidate);
    }

    /// <summary>
    /// グループヘッダ ContextMenu の「アセットを削除...」クリック。確認ダイアログを表示し、
    /// 承認されたら親 Window 経由で <c>MainWindowViewModel.AssetLibrary.DeleteByIdAsync</c> を呼ぶ。
    /// 削除は cascade で関連バリアント・配置を消す（既存の <see cref="DeleteImageAssetUseCase"/> 挙動）。
    /// </summary>
    private async void OnDeleteAssetClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        if (mi.DataContext is not CandidateGroupViewModel group) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (owner.DataContext is not MainWindowViewModel mainVm) return;

        var variantCount = group.Variants.Count;
        var message =
            $"アセット「{group.AssetFilename}」と紐づく {variantCount} バリアントを削除します。\n" +
            "このアセットを参照している全配置も削除され、操作履歴はリセットされます。\n" +
            "この操作は元に戻せません。続行しますか？";
        var confirmed = await ConfirmDialog.ShowAsync(owner, "アセットの削除", message, "削除");
        if (!confirmed) return;

        await mainVm.AssetLibrary.DeleteByIdAsync(group.AssetId);
    }
}
