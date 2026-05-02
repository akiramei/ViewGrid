using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using Xunit;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class CopyListViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private CopyListViewModel _vm = null!;
    private WeakReferenceMessenger _messenger = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        var create = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
        var update = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
        _messenger = new WeakReferenceMessenger();
        var history = new ViewGrid.Application.History.UndoRedoService();
        _vm = new CopyListViewModel(
            _fx.CopyRepository, _fx.AssetRepository, _fx.Thumbnails, _fx.Storage,
            create, update, _messenger, history,
            NullLogger<CopyListViewModel>.Instance);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task LoadForAssetAsync_With_Null_Clears_List_And_HasAsset_Is_False()
    {
        await _vm.LoadForAssetAsync(null);

        _vm.Copies.Should().BeEmpty();
        _vm.SelectedCopy.Should().BeNull();
        _vm.HasAsset.Should().BeFalse();
    }

    [Fact]
    public async Task LoadForAssetAsync_Populates_From_Repository_And_Selects_First()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "one");
        await _fx.SeedCopyAsync(asset.Id, copyName: "two");

        await _vm.LoadForAssetAsync(asset.Id);

        _vm.Copies.Should().HaveCount(2);
        _vm.HasAsset.Should().BeTrue();
        _vm.SelectedCopy.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateCopyAsync_Adds_And_Selects_New_Copy()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);

        await _vm.CreateCopyAsync();

        _vm.Copies.Should().HaveCount(1);
        _vm.SelectedCopy.Should().NotBeNull();
        _vm.SelectedCopy!.DisplayName.Should().Be("バリアント 1");
    }

    [Fact]
    public async Task CreateCopyAsync_Noop_When_No_Asset_Selected()
    {
        await _vm.LoadForAssetAsync(null);

        await _vm.CreateCopyAsync();

        _vm.Copies.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSelectedCopyAsync_Removes_Selection_And_Selects_Next()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "a");
        await _fx.SeedCopyAsync(asset.Id, copyName: "b");
        await _vm.LoadForAssetAsync(asset.Id);

        var first = _vm.SelectedCopy!;
        await _vm.DeleteSelectedCopyAsync();

        _vm.Copies.Should().NotContain(first);
        _vm.Copies.Should().HaveCount(1);
        _vm.SelectedCopy.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteSelectedCopyAsync_Noop_When_No_Selection()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);
        _vm.SelectedCopy = null;

        await _vm.DeleteSelectedCopyAsync();

        _vm.Copies.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCopyAsync_Sends_CopyLibraryChangedMessage()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);

        var receivedCount = 0;
        var listener = new object();
        _messenger.Register<CopyLibraryChangedMessage>(listener, (_, _) => receivedCount++);

        try
        {
            await _vm.CreateCopyAsync();
            receivedCount.Should().Be(1);
        }
        finally
        {
            _messenger.UnregisterAll(listener);
        }
    }

    [Fact]
    public async Task DeleteSelectedCopyAsync_Sends_CopyLibraryChangedMessage()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "x");
        await _vm.LoadForAssetAsync(asset.Id);

        var receivedCount = 0;
        var listener = new object();
        _messenger.Register<CopyLibraryChangedMessage>(listener, (_, _) => receivedCount++);

        try
        {
            await _vm.DeleteSelectedCopyAsync();
            receivedCount.Should().Be(1);
        }
        finally
        {
            _messenger.UnregisterAll(listener);
        }
    }

    [Fact]
    public async Task CreateCopyAsync_Uses_Custom_Name_When_Provided()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);

        await _vm.CreateCopyAsync("マイコピー");

        _vm.Copies.Should().ContainSingle();
        _vm.SelectedCopy!.CopyName.Should().Be("マイコピー");
    }

    [Fact]
    public async Task CreateCopyAsync_Trims_Whitespace_From_Custom_Name()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);

        await _vm.CreateCopyAsync("  名前付き  ");

        _vm.SelectedCopy!.CopyName.Should().Be("名前付き");
    }

    [Fact]
    public async Task CommitCreateAsync_Uses_DraftCopyName()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);
        _vm.BeginCreate();
        _vm.IsCreating.Should().BeTrue();
        _vm.DraftCopyName = "下書き名";

        await _vm.CommitCreateAsync();

        _vm.Copies.Should().ContainSingle();
        _vm.SelectedCopy!.CopyName.Should().Be("下書き名");
        _vm.IsCreating.Should().BeFalse();
        _vm.DraftCopyName.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitCreateAsync_Falls_Back_To_Auto_Name_When_Draft_Empty()
    {
        var asset = await _fx.SeedAssetAsync();
        await _vm.LoadForAssetAsync(asset.Id);
        _vm.BeginCreate();
        // DraftCopyName is empty by default

        await _vm.CommitCreateAsync();

        _vm.SelectedCopy!.CopyName.Should().Be("バリアント 1");
    }

    [Fact]
    public void CancelCreate_Closes_Flyout_And_Clears_Draft()
    {
        _vm.BeginCreate();
        _vm.DraftCopyName = "破棄予定";

        _vm.CancelCreate();

        _vm.IsCreating.Should().BeFalse();
        _vm.DraftCopyName.Should().BeEmpty();
    }

    [Fact]
    public async Task BeginEdit_Sets_IsEditing_True_And_Copies_CurrentName()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "current");
        await _vm.LoadForAssetAsync(asset.Id);
        var item = _vm.Copies[0];

        _vm.BeginEdit(item);

        item.IsEditing.Should().BeTrue();
        item.EditingName.Should().Be("current");
    }

    [Fact]
    public async Task CancelEdit_Resets_IsEditing_And_EditingName()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "x");
        await _vm.LoadForAssetAsync(asset.Id);
        var item = _vm.Copies[0];
        _vm.BeginEdit(item);
        item.EditingName = "modified";

        _vm.CancelEdit(item);

        item.IsEditing.Should().BeFalse();
        item.EditingName.Should().BeNull();
        item.CopyName.Should().Be("x"); // 元の名前は維持
    }

    [Fact]
    public async Task CommitEditAsync_Updates_CopyName_And_Persists()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "before");
        await _vm.LoadForAssetAsync(asset.Id);
        var item = _vm.Copies[0];
        _vm.BeginEdit(item);
        item.EditingName = "after";

        await _vm.CommitEditAsync(item);

        item.IsEditing.Should().BeFalse();
        item.CopyName.Should().Be("after");
        var persisted = await _fx.CopyRepository.FindByIdAsync(copy.Id);
        persisted!.CopyName.Should().Be("after");
    }

    [Fact]
    public async Task CommitEditAsync_NoOp_When_Name_Unchanged()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "same");
        await _vm.LoadForAssetAsync(asset.Id);
        var item = _vm.Copies[0];
        _vm.BeginEdit(item);
        item.EditingName = "same";

        await _vm.CommitEditAsync(item);

        item.IsEditing.Should().BeFalse();
        item.CopyName.Should().Be("same");
        // 履歴に積まれていない事実は別テスト（GridAndCopyCommandTests）で UpdateImageCopyCommand 経路を
        // 直接検証しているため、ここでは VM 状態だけを確認する。
    }

    [Fact]
    public async Task CommitEditAsync_Trims_Whitespace_To_Null()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "x");
        await _vm.LoadForAssetAsync(asset.Id);
        var item = _vm.Copies[0];
        _vm.BeginEdit(item);
        item.EditingName = "   ";

        await _vm.CommitEditAsync(item);

        item.CopyName.Should().BeNull();
        item.DisplayName.Should().Be("既定");
    }

    [Fact]
    public async Task CommitEditAsync_Sends_CopyLibraryChangedMessage_On_Change()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "before");
        await _vm.LoadForAssetAsync(asset.Id);
        var item = _vm.Copies[0];
        _vm.BeginEdit(item);
        item.EditingName = "after";

        var receivedCount = 0;
        var listener = new object();
        _messenger.Register<CopyLibraryChangedMessage>(listener, (_, _) => receivedCount++);

        try
        {
            await _vm.CommitEditAsync(item);
            receivedCount.Should().Be(1);
        }
        finally
        {
            _messenger.UnregisterAll(listener);
        }
    }

    [Fact]
    public async Task BeginEdit_On_Other_Item_Cancels_Existing_Edit()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id, copyName: "a");
        await _fx.SeedCopyAsync(asset.Id, copyName: "b");
        await _vm.LoadForAssetAsync(asset.Id);
        var first = _vm.Copies[0];
        var second = _vm.Copies[1];
        _vm.BeginEdit(first);
        first.IsEditing.Should().BeTrue();

        _vm.BeginEdit(second);

        first.IsEditing.Should().BeFalse();
        second.IsEditing.Should().BeTrue();
    }
}
