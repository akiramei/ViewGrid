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
        // ListBox 配下のアイテムから発生するイベントをトンネルでフックする
        CandidateList.AddHandler(PointerPressedEvent, OnCandidatePointerPressed, RoutingStrategies.Tunnel);
        CandidateList.AddHandler(PointerMovedEvent, OnCandidatePointerMoved, RoutingStrategies.Tunnel);
        CandidateList.AddHandler(PointerReleasedEvent, OnCandidatePointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnCandidatePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CandidateList).Properties.IsLeftButtonPressed)
            return;

        var item = FindCandidateItem(e.Source);
        if (item is null)
            return;

        // インラインリネーム編集中はサムネ D&D を無効化（TextBox 内のテキスト選択を
        // 誤ってドラッグ操作と解釈しないため）
        if (item.IsEditing) return;

        _pressOrigin = e.GetPosition(CandidateList);
        _pressItem = item;
        _pressEvent = e;
    }

    private async void OnCandidatePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressOrigin is null || _pressItem is null || _pressEvent is null)
            return;

        var current = e.GetPosition(CandidateList);
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

    private static CopyCandidateViewModel? FindCandidateItem(object? source)
    {
        if (source is not Visual visual)
            return null;

        // ListBoxItem を遡って DataContext から候補 VM を取得
        var current = visual;
        while (current is not null)
        {
            if (current is ListBoxItem lbi && lbi.DataContext is CopyCandidateViewModel vm)
                return vm;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ─── 配置ファースト UI 第 2 段階 (Stage 2): バリアント候補のインラインリネーム ───

    /// <summary>
    /// ダブルクリックでインラインリネーム編集を開始する（CopyListView と同パターン）。
    /// <para>
    /// 既に編集中の場合は no-op。編集中 TextBox 内でユーザーがテキスト選択のために
    /// ダブルクリックすると本ハンドラまでイベントがバブルアップして
    /// <see cref="GridWorkspaceViewModel.BeginEditCandidate"/> を再度呼び、
    /// <see cref="CopyCandidateViewModel.EditingName"/> が保存済み <c>CopyName</c> に
    /// リセットされる（= 入力中のリネーム文字が消える）回帰を防ぐ。
    /// </para>
    /// </summary>
    private void OnCandidateListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not GridWorkspaceViewModel vm) return;
        if (e.Source is not Control src) return;

        var item = src as ListBoxItem ?? src.FindAncestorOfType<ListBoxItem>();
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
}
