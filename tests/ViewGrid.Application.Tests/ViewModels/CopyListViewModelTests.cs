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
        _messenger = new WeakReferenceMessenger();
        var history = new ViewGrid.Application.History.UndoRedoService();
        _vm = new CopyListViewModel(
            _fx.CopyRepository, _fx.AssetRepository, _fx.Thumbnails, _fx.Storage,
            create, _messenger, history,
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
        _vm.SelectedCopy!.DisplayName.Should().Be("コピー 1");
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
}
