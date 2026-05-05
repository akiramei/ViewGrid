using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class AssetLibraryViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private AssetLibraryViewModel _vm = null!;
    private IFilePickerService _picker = null!;
    private WeakReferenceMessenger _messenger = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _picker = Substitute.For<IFilePickerService>();

        var import = new ImportImageUseCase(
            hasher: new Sha256ImageHasher(),
            prober: new SkiaImageProber(),
            storage: _fx.Storage,
            thumbnailService: _fx.Thumbnails,
            assetRepository: _fx.AssetRepository,
            copyRepository: _fx.CopyRepository,
            settings: _fx.AppSettings,
            logger: NullLogger<ImportImageUseCase>.Instance);

        var delete = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);

        _messenger = new WeakReferenceMessenger();
        var history = new ViewGrid.Application.History.UndoRedoService();
        _vm = new AssetLibraryViewModel(
            import,
            delete,
            _fx.AssetRepository,
            _fx.Thumbnails,
            _picker,
            _messenger,
            history,
            NullLogger<AssetLibraryViewModel>.Instance);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task LoadAsync_Populates_Assets_From_Repository()
    {
        await _fx.SeedAssetAsync(fileHash: "a".PadRight(64, '0'));
        await _fx.SeedAssetAsync(fileHash: "b".PadRight(64, '0'));

        await _vm.LoadAsync();

        _vm.Assets.Should().HaveCount(2);
        _vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task AddFilesAsync_Imports_New_Files_And_Refreshes_List()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);

            _vm.Assets.Should().HaveCount(1);
            _vm.StatusMessage.Should().Contain("1 件追加");
            _vm.Assets[0].Width.Should().Be(100);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AddFilesAsync_Reports_Duplicates_For_Identical_Files()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            await _vm.AddFilesAsync([file]);

            _vm.Assets.Should().HaveCount(1);
            _vm.StatusMessage.Should().Contain("重複");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AddFilesAsync_Counts_Failures_For_Invalid_Files()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"viewgrid-bogus-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(bogus, "not an image");
        try
        {
            await _vm.AddFilesAsync([bogus]);

            _vm.Assets.Should().BeEmpty();
            _vm.StatusMessage.Should().Contain("失敗");
        }
        finally
        {
            File.Delete(bogus);
        }
    }

    [Fact]
    public async Task PickFilesAndImportAsync_Delegates_To_FilePickerService()
    {
        var file = TestImageFactory.WritePngToTempFile(80, 80);
        try
        {
            _picker.PickImagesAsync(Arg.Any<CancellationToken>()).Returns([file]);

            await _vm.PickFilesAndImportAsync();

            await _picker.Received(1).PickImagesAsync(Arg.Any<CancellationToken>());
            _vm.Assets.Should().HaveCount(1);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task PickFilesAndImportAsync_Noop_When_User_Cancels()
    {
        _picker.PickImagesAsync(Arg.Any<CancellationToken>()).Returns([]);

        await _vm.PickFilesAndImportAsync();

        _vm.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSelectedAsync_Removes_Selected_Asset()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            _vm.SelectedAsset = _vm.Assets[0];

            await _vm.DeleteSelectedAsync();

            _vm.Assets.Should().BeEmpty();
            _vm.SelectedAsset.Should().BeNull();
            _vm.StatusMessage.Should().Contain("削除しました");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AddFilesAsync_Sends_CopyLibraryChangedMessage_On_Success()
    {
        var receivedCount = 0;
        var listener = new object();
        _messenger.Register<CopyLibraryChangedMessage>(listener, (_, _) => receivedCount++);

        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            receivedCount.Should().Be(1);
        }
        finally
        {
            File.Delete(file);
            _messenger.UnregisterAll(listener);
        }
    }

    [Fact]
    public async Task DeleteSelectedAsync_Sends_CopyLibraryChangedMessage()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            _vm.SelectedAsset = _vm.Assets[0];

            var receivedCount = 0;
            var listener = new object();
            _messenger.Register<CopyLibraryChangedMessage>(listener, (_, _) => receivedCount++);

            try
            {
                await _vm.DeleteSelectedAsync();
                receivedCount.Should().Be(1);
            }
            finally
            {
                _messenger.UnregisterAll(listener);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task DeleteSelectedAsync_NoOp_When_No_Selection()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            _vm.SelectedAsset = null;

            await _vm.DeleteSelectedAsync();

            _vm.Assets.Should().HaveCount(1);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// DeleteByIdAsync は Selection を介さず指定 Id のアセットを削除する。
    /// 配置タブ候補ツリーの右クリックメニュー経由で呼ばれる経路。
    /// </summary>
    [Fact]
    public async Task DeleteByIdAsync_Removes_Asset_And_Sends_LibraryChangedMessage()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            var target = _vm.Assets.Single();
            var received = false;
            _messenger.Register<CopyLibraryChangedMessage>(this, (_, _) => received = true);

            var ok = await _vm.DeleteByIdAsync(target.AssetId);

            ok.Should().BeTrue();
            _vm.Assets.Should().BeEmpty();
            _vm.StatusMessage.Should().Contain("削除");
            received.Should().BeTrue();
        }
        finally
        {
            File.Delete(file);
            _messenger.UnregisterAll(this);
        }
    }

    /// <summary>未知の AssetId に対しては Error を返し StatusMessage に理由を出す。</summary>
    [Fact]
    public async Task DeleteByIdAsync_Returns_False_For_Unknown_AssetId()
    {
        var ok = await _vm.DeleteByIdAsync(System.Guid.NewGuid());

        ok.Should().BeFalse();
        _vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    /// <summary>削除対象のアセットが SelectedAsset / SelectedAssets に含まれていれば一緒に解除する。</summary>
    [Fact]
    public async Task DeleteByIdAsync_Clears_Selection_If_Target_Was_Selected()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            var target = _vm.Assets.Single();
            _vm.SelectedAsset = target;

            await _vm.DeleteByIdAsync(target.AssetId);

            _vm.SelectedAsset.Should().BeNull();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
