using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.History;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;

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
        var occupy = new UpdatePlacementOccupySizeUseCase(_fx.PlacementRepository, _fx.GridRepository);
        var fork = new ForkPlacementVariantUseCase(_fx.CopyRepository, _fx.PlacementRepository);
        var history = new UndoRedoService();
        var updateCopy = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
        var copyProperties = new CopyPropertiesViewModel(
            updateCopy, history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver,
            NullLogger<CopyPropertiesViewModel>.Instance);
        _vm = new PlacementInspectorViewModel(
            offset,
            occupy,
            fork,
            _fx.PlacementRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _fx.Thumbnails,
            _fx.Storage,
            copyProperties,
            history,
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
        var (item, _, _) = await SeedAndPlaceAsync();

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
        var (item, _, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 50;

        _vm.IsDirty.Should().BeTrue();
        _vm.SaveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_Persists_PixelOffset_To_Placement()
    {
        var (item, _, gridVm) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item, gridVm);

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
        var (item, _, gridVm) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item, gridVm);

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
        var (item, _, _) = await SeedAndPlaceAsync();
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
        var (item, _, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        _vm.PixelOffsetX = 50;
        _vm.PixelOffsetY = 75;

        _vm.ResetPixelOffsetCommand.Execute(null);

        _vm.PixelOffsetX.Should().Be(0);
        _vm.PixelOffsetY.Should().Be(0);
    }

    /// <summary>
    /// Stage 3: AttachAsync は CopyProperties に当該 placement の variant を直接 attach する
    /// （旧: NavigateToCopyPropertiesMessage 経由でタブ切替）。inline embed された
    /// CopyPropertiesView がそのまま編集対象を表示する基盤。
    /// </summary>
    [Fact]
    public async Task AttachAsync_Attaches_CopyProperties_To_Placement_Variant()
    {
        var (item, copy, _) = await SeedAndPlaceAsync();

        await _vm.AttachAsync(item);

        _vm.CopyProperties.HasCopy.Should().BeTrue();
        _vm.CopyProperties.AttachedSourceForTests.Should().NotBeNull();
        _vm.CopyProperties.AttachedSourceForTests!.CopyId.Should().Be(copy.Id);
    }

    /// <summary>AttachAsync(null) で CopyProperties も detach（HasCopy=false）。</summary>
    [Fact]
    public async Task AttachAsync_Null_Detaches_CopyProperties()
    {
        var (item, _, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);
        _vm.CopyProperties.HasCopy.Should().BeTrue();

        await _vm.AttachAsync(null);

        _vm.CopyProperties.HasCopy.Should().BeFalse();
    }

    private async Task<(PlacementItemViewModel item, ImageCopy copy, GridCanvasItemViewModel gridVm)> SeedAndPlaceAsync()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedGridAsync(2, 2);
        var p = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        p.IsError.Should().BeFalse();
        var item = new PlacementItemViewModel(p.Value, copy, asset, null);
        var gridVm = new GridCanvasItemViewModel(grid);
        return (item, copy, gridVm);
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
        var (item, _, _) = await SeedAndPlaceAsync();
        var grid = await _fx.GridRepository.FindByIdAsync(item.GridId);
        var gridVm = new GridCanvasItemViewModel(grid!);

        await _vm.AttachAsync(item, gridVm);

        _vm.ImageDrawSizeLabel.Should().Contain("200×200");
    }

    [Fact]
    public async Task AttachAsync_Clears_ImageDrawSizeLabel_When_Source_Is_Null()
    {
        var (item, _, _) = await SeedAndPlaceAsync();
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
        // Shift+ドラッグ・Ctrl+Arrow など UI チャネルは PlacementItemViewModel.PixelOffset を
        // 直接更新する「Inspector 数値直接入力の直感的な代替」。Inspector はその変更を
        // 同期表示し、IsDirty=true を立て、保存ボタン押下で 1 履歴エントリにまとめて永続化する。
        var (item, _, _) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item);

        item.PixelOffsetX = 123;
        item.PixelOffsetY = -45;

        _vm.PixelOffsetX.Should().Be(123);
        _vm.PixelOffsetY.Should().Be(-45);
        _vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task RevertAsync_Restores_Source_And_Buffer_From_Db_Even_When_Source_Was_Mutated()
    {
        // Shift+ドラッグ等で source.PixelOffset を直接書き換えた状態から、
        // RevertAsync で「DB の永続化値」に戻ること。
        var (item, _, gridVm) = await SeedAndPlaceAsync();
        await _vm.AttachAsync(item, gridVm);

        // DB に値を書いてから永続化値を確定する（Save 経由）
        _vm.PixelOffsetX = 30;
        _vm.PixelOffsetY = 40;
        await _vm.SaveAsync();
        _vm.IsDirty.Should().BeFalse();

        // Shift+ドラッグ相当: source を直接書き換え（Inspector に IsDirty=true が立つ）
        item.PixelOffsetX = 999;
        item.PixelOffsetY = -999;
        _vm.IsDirty.Should().BeTrue();

        await _vm.RevertAsync();

        _vm.IsDirty.Should().BeFalse();
        // source 自体も DB の値に戻る（View が PropertyChanged で追従するため）
        item.PixelOffsetX.Should().Be(30);
        item.PixelOffsetY.Should().Be(40);
        _vm.PixelOffsetX.Should().Be(30);
        _vm.PixelOffsetY.Should().Be(40);
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
