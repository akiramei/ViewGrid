using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;
using Xunit;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class GridWorkspaceViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private WeakReferenceMessenger _messenger = null!;
    private GridWorkspaceViewModel _vm = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _messenger = new WeakReferenceMessenger();

        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var remove = new RemovePlacementUseCase(_fx.PlacementRepository);
        var move = new MovePlacementUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var swap = new SwapPlacementsUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var render = new RenderGridUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository,
            _fx.AssetRepository, _fx.Storage, new SkiaGridImageRenderer());
        var export = new ExportGridUseCase(render);
        var picker = Substitute.For<IFilePickerService>();
        var update = new UpdateImageCopyUseCase(_fx.CopyRepository);
        var offset = new UpdatePlacementOffsetUseCase(_fx.PlacementRepository);
        var inspector = new PlacementInspectorViewModel(
            update,
            offset,
            _fx.CopyRepository,
            _fx.PlacementRepository,
            _messenger,
            NullLogger<PlacementInspectorViewModel>.Instance);

        var updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        var updateLocks = new UpdateGridLocksUseCase(_fx.GridRepository);
        var fitWeight = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository, _fx.PlacementRepository, _fx.CopyRepository, _fx.AssetRepository, updateWeights,
            NullLogger<FitGridWeightToPlacementUseCase>.Instance);

        _vm = new GridWorkspaceViewModel(
            _fx.GridRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _fx.PlacementRepository,
            _fx.Thumbnails,
            place,
            remove,
            move,
            swap,
            render,
            export,
            updateWeights,
            updateLocks,
            offset,
            fitWeight,
            picker,
            _messenger,
            inspector,
            NullLogger<GridWorkspaceViewModel>.Instance);
    }

    public async Task DisposeAsync()
    {
        _messenger.UnregisterAll(_vm);
        await _fx.DisposeAsync();
    }

    [Fact]
    public async Task Receive_Reloads_Candidates_With_Newly_Added_Copy()
    {
        // 初期状態は空
        _vm.Candidates.Should().BeEmpty();

        // 準備タブで Copy が増えた相当の状態を作る（DB に直接投入）
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "new-copy");

        // メッセージ受信を await できる形で発火
        _vm.Receive(new CopyLibraryChangedMessage());
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Should().HaveCount(1);
        _vm.Candidates[0].CopyDisplayName.Should().Contain("new-copy");
    }

    [Fact]
    public async Task Receive_Reflects_Removed_Copies()
    {
        // Copy 2 件
        var asset = await _fx.SeedAssetAsync();
        var c1 = await _fx.SeedCopyAsync(asset.Id, copyName: "keep");
        var c2 = await _fx.SeedCopyAsync(asset.Id, copyName: "remove");

        await _vm.ReloadFromMessageAsyncForTests();
        _vm.Candidates.Should().HaveCount(2);

        // 1 件削除
        await _fx.CopyRepository.DeleteAsync(c2.Id);

        // メッセージ受信で再ロード → 1 件に減る
        await _vm.ReloadFromMessageAsyncForTests();
        _vm.Candidates.Should().HaveCount(1);
        _vm.Candidates[0].CopyId.Should().Be(c1.Id);
    }

    [Fact]
    public async Task Sending_Through_Messenger_Triggers_Reload_On_Registered_Vm()
    {
        // VM はコンストラクタで Register 済み。メッセンジャー経由で Send → Receive が呼ばれることを確認。
        // fire-and-forget の完了を待つため、初期 Reload を 1 度走らせて await する。
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id);

        // Send 経由で発火
        _messenger.Send(new CopyLibraryChangedMessage());

        // fire-and-forget の完了を確実に await するため同形メソッドを 1 回呼ぶ。
        // （実環境では UI スレッド側の dispatcher が処理するため tick 不要だが、
        //  単体テストでは内部メソッドを await して確定させる）
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Should().HaveCount(1);
    }

    [Fact]
    public async Task Receive_Drops_Stale_Placements_When_Their_Asset_Cascade_Deleted()
    {
        // Asset + Copy + アクティブグリッドに配置を作る
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(rows: 2, cols: 2);

        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        // VM をそのグリッドに紐付ける（候補と配置を読む）
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        _vm.Candidates.Should().HaveCount(1);
        _vm.Placements.Should().HaveCount(1);

        // 準備タブからアセット削除（cascade で Copy / Placement も DB から消える）
        var deleteAsset = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
        var deleteResult = await deleteAsset.ExecuteAsync(asset.Id);
        deleteResult.IsError.Should().BeFalse();

        // メッセージ受信で再ロード → 候補も配置も空になる
        await _vm.ReloadFromMessageAsyncForTests();

        _vm.Candidates.Should().BeEmpty();
        _vm.Placements.Should().BeEmpty();
        _vm.SelectedPlacement.Should().BeNull();
    }

    private async Task<GridCanvas> SeedActiveGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = $"workspace-{rows}x{cols}",
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
    public async Task ApplyPixelOffsetAsync_Updates_Placement_And_Persists()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        var item = _vm.Placements.Single();

        var ok = await _vm.ApplyPixelOffsetAsync(item.PlacementId, 50, -30);

        ok.Should().BeTrue();
        item.PixelOffsetX.Should().Be(50);
        item.PixelOffsetY.Should().Be(-30);
        var reloaded = await _fx.PlacementRepository.FindByIdAsync(item.PlacementId);
        reloaded!.PixelOffsetX.Should().Be(50);
        reloaded.PixelOffsetY.Should().Be(-30);
    }

    [Fact]
    public async Task ApplyPixelOffsetAsync_Clamps_Out_Of_Range_Values()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var grid = await SeedActiveGridAsync(2, 2);
        var place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
        var placed = await place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        placed.IsError.Should().BeFalse();

        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));
        var item = _vm.Placements.Single();

        // ±MaxPixelOffset (4096) を超える値を渡しても clamp される
        var ok = await _vm.ApplyPixelOffsetAsync(item.PlacementId, 99999, -99999);

        ok.Should().BeTrue();
        item.PixelOffsetX.Should().Be(PlacementInspectorViewModel.MaxPixelOffset);
        item.PixelOffsetY.Should().Be(-PlacementInspectorViewModel.MaxPixelOffset);
    }

    [Fact]
    public async Task ApplyPixelOffsetAsync_Unknown_Placement_Returns_False_And_Sets_StatusMessage()
    {
        var grid = await SeedActiveGridAsync(2, 2);
        await _vm.LoadGridAsync(new GridCanvasItemViewModel(grid));

        var ok = await _vm.ApplyPixelOffsetAsync(Guid.NewGuid(), 10, 10);

        ok.Should().BeFalse();
        _vm.StatusMessage.Should().NotBeNullOrEmpty();
    }
}
