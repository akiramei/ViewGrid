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
}
