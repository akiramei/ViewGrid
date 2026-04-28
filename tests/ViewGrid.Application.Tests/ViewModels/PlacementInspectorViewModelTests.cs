using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.ViewModels;

/// <summary>
/// PlacementInspectorViewModel は配置固有の <see cref="PlacementItemViewModel.PixelOffsetX"/> /
/// <see cref="PlacementItemViewModel.PixelOffsetY"/> のみを編集する。
/// 共有特性（Rotation/Flip/Scaling/Trim/Align/Occupy）の編集は CopyPropertiesViewModel に移譲され、
/// Inspector からは「特性を編集 →」コマンドで <see cref="NavigateToCopyPropertiesMessage"/> を送る。
/// </summary>
public sealed class PlacementInspectorViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private WeakReferenceMessenger _messenger = null!;
    private PlacementInspectorViewModel _vm = null!;
    private PlaceImageCopyUseCase _place = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _messenger = new WeakReferenceMessenger();
        var offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        _vm = new PlacementInspectorViewModel(
            offset,
            _messenger,
            NullLogger<PlacementInspectorViewModel>.Instance);
        _place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task AttachAsync_With_Null_Resets_State_And_HasPlacement_Becomes_False()
    {
        await _vm.AttachAsync(null);

        _vm.HasPlacement.Should().BeFalse();
        _vm.HeaderLabel.Should().BeEmpty();
        _vm.PositionLabel.Should().BeEmpty();
        _vm.PixelOffsetX.Should().Be(0);
        _vm.PixelOffsetY.Should().Be(0);
        _vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task AttachAsync_Loads_Placement_Values_Without_Marking_Dirty()
    {
        var (item, _) = await SeedAndPlaceAsync();

        await _vm.AttachAsync(item);

        _vm.HasPlacement.Should().BeTrue();
        _vm.IsDirty.Should().BeFalse();
        _vm.PixelOffsetX.Should().Be(item.PixelOffsetX);
        _vm.PixelOffsetY.Should().Be(item.PixelOffsetY);
        _vm.HeaderLabel.Should().Be(item.Label);
    }

    [Fact]
    public async Task Editing_PixelOffset_Marks_Dirty_And_Enables_Save()
    {
        var (item, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 50;

        _vm.IsDirty.Should().BeTrue();
        _vm.SaveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_Persists_PixelOffset_To_Placement()
    {
        var (item, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 100;
        _vm.PixelOffsetY = -25;

        await _vm.SaveAsync();

        _vm.IsDirty.Should().BeFalse();
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(item.PlacementId);
        reloaded!.PixelOffsetX.Should().Be(100);
        reloaded.PixelOffsetY.Should().Be(-25);
        // VM 上の source 側にも同期されている
        item.PixelOffsetX.Should().Be(100);
        item.PixelOffsetY.Should().Be(-25);
    }

    [Fact]
    public async Task SaveAsync_Clamps_PixelOffset_To_Max()
    {
        var (item, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 99_999;
        _vm.PixelOffsetY = -99_999;

        await _vm.SaveAsync();

        var reloaded = await _fx.PlacementRepository.FindByIdAsync(item.PlacementId);
        reloaded!.PixelOffsetX.Should().Be(PlacementInspectorViewModel.MaxPixelOffset);
        reloaded.PixelOffsetY.Should().Be(-PlacementInspectorViewModel.MaxPixelOffset);
    }

    [Fact]
    public async Task RevertAsync_Restores_Edit_Buffer_From_Source()
    {
        var (item, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 200;
        _vm.IsDirty.Should().BeTrue();

        await _vm.RevertAsync();

        _vm.PixelOffsetX.Should().Be(item.PixelOffsetX);
        _vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPixelOffsetCommand_Sets_Both_Axes_To_Zero()
    {
        var (item, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 50;
        _vm.PixelOffsetY = 75;

        _vm.ResetPixelOffsetCommand.Execute(null);

        _vm.PixelOffsetX.Should().Be(0);
        _vm.PixelOffsetY.Should().Be(0);
    }

    [Fact]
    public async Task EditCopyPropertiesCommand_Sends_Navigate_Message_With_Asset_And_Copy_Ids()
    {
        var (item, copy) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        NavigateToCopyPropertiesMessage? received = null;
        var listener = new object();
        _messenger.Register<NavigateToCopyPropertiesMessage>(listener, (_, m) => received = m);

        try
        {
            _vm.EditCopyPropertiesCommand.Execute(null);

            received.Should().NotBeNull();
            received!.AssetId.Should().Be(copy.AssetId);
            received.CopyId.Should().Be(copy.Id);
        }
        finally
        {
            _messenger.UnregisterAll(listener);
        }
    }

    [Fact]
    public void EditCopyPropertiesCommand_Is_Disabled_When_No_Placement_Attached()
    {
        _vm.EditCopyPropertiesCommand.CanExecute(null).Should().BeFalse();
    }

    private async Task<(PlacementItemViewModel item, ImageCopy copy)> SeedAndPlaceAsync()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedGridAsync(2, 2);
        var p = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        p.IsError.Should().BeFalse();
        var item = new PlacementItemViewModel(p.Value, copy, asset, null);
        return (item, copy);
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(400, 400),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();
        return grid;
    }

    [Fact]
    public async Task AttachAsync_Populates_ImageDrawSizeLabel_When_Grid_Is_Provided()
    {
        // 400x400 キャンバスの 2x2 グリッド = 各セル 200x200。OccupySize=1×1 なので 200×200 px。
        var (item, _) = await SeedAndPlaceAsync();
        var grid = await _fx.GridRepository.FindByIdAsync(item.GridId);
        var gridVm = new GridCanvasItemViewModel(grid!);

        await _vm.AttachAsync(item, gridVm);

        _vm.ImageDrawSizeLabel.Should().Contain("200×200");
    }

    [Fact]
    public async Task AttachAsync_Clears_ImageDrawSizeLabel_When_Source_Is_Null()
    {
        var (item, _) = await SeedAndPlaceAsync();
        var grid = await _fx.GridRepository.FindByIdAsync(item.GridId);
        var gridVm = new GridCanvasItemViewModel(grid!);
        await _vm.AttachAsync(item, gridVm);
        _vm.ImageDrawSizeLabel.Should().NotBeEmpty();

        await _vm.AttachAsync(null, gridVm);

        _vm.ImageDrawSizeLabel.Should().BeEmpty();
    }

    [Fact]
    public async Task External_PixelOffset_Change_On_Source_Syncs_Inspector_And_Marks_Dirty()
    {
        // Shift+ドラッグなど外部から source の PixelOffset が更新されたとき、
        // Inspector の表示が追従し、IsDirty=true となる（保存ボタン経由で永続化する設計）。
        // 以前は自動保存していたが、編集と保存の責務分離が崩れる UX 上の違和感があったため改修。
        var (item, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        item.PixelOffsetX = 123;
        item.PixelOffsetY = -45;

        _vm.PixelOffsetX.Should().Be(123);
        _vm.PixelOffsetY.Should().Be(-45);
        _vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Reattaching_To_New_Source_Stops_Listening_To_Old_Source()
    {
        // 別 placement に Attach 切り替え後、古い source の変化に反応しない。
        // 同じ asset/copy を別セルに 2 配置してからそれぞれを VM 化（同一 SeedAndPlaceAsync 連打は
        // 同一 hash の asset 重複でエラーになるため、placement だけ別途追加する）。
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedGridAsync(2, 2);
        var p1 = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        var p2 = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(1, 0));
        var item1 = new PlacementItemViewModel(p1.Value, copy, asset, null);
        var item2 = new PlacementItemViewModel(p2.Value, copy, asset, null);

        await _vm.AttachAsync(item1);
        await _vm.AttachAsync(item2);

        var beforeX = _vm.PixelOffsetX;
        item1.PixelOffsetX = 999;

        _vm.PixelOffsetX.Should().Be(beforeX);
    }
}
