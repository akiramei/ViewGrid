using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViewGrid.Application.Localization;
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
    private ViewGrid.Application.History.UndoRedoService _history = null!;

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
        _history = history;
        _vm = new AssetLibraryViewModel(
            import,
            delete,
            _fx.AssetRepository,
            _fx.Thumbnails,
            _picker,
            _messenger,
            history,
            new NullLocalizationService(),
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
            _vm.StatusMessage.Should().Contain("Status_AssetImportedFmt(1)");
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
            _vm.StatusMessage.Should().Contain("Status_AssetDuplicatedFmt");
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
            _vm.StatusMessage.Should().Contain("Status_AssetImportFailedFmt");
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
            _vm.StatusMessage.Should().Contain("Status_AssetDeletedSingleFmt");
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
            _vm.StatusMessage.Should().Contain("Status_AssetDeletedSingleFmt");
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

    // ─── 取り込み結果の内訳・履歴・再試行 (ユーザビリティ評価) ──────────────────────

    private async Task PushAnUndoableEditAsync()
    {
        var command = Substitute.For<ViewGrid.Application.History.IUndoableCommand>();
        command.Description.Returns("edit");
        command.ExecuteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<ErrorOr.Success>>(ErrorOr.Result.Success));
        (await _history.ExecuteAsync(command)).IsError.Should().BeFalse();
        _history.CanUndo.Should().BeTrue();
    }

    [Fact]
    public async Task Importing_Only_A_Duplicate_Keeps_The_Undo_History_And_Does_Not_Notify_The_Library()
    {
        var file = TestImageFactory.WritePngToTempFile(100, 100);
        try
        {
            await _vm.AddFilesAsync([file]);
            await PushAnUndoableEditAsync();
            var notified = 0;
            _messenger.Register<object, CopyLibraryChangedMessage>(this, (_, _) => notified++);

            await _vm.AddFilesAsync([file]); // 同じ画像 = 重複 (何も変わらない)

            _history.CanUndo.Should().BeTrue("重複取り込みは参照関係を変えないので、 それ以前の編集を Undo できるまま残す");
            notified.Should().Be(0);
            _vm.StatusMessage.Should().Contain("Status_AssetDuplicatedFmt");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Importing_A_New_Image_Clears_The_History_And_Notifies_The_Library()
    {
        var first = TestImageFactory.WritePngToTempFile(100, 100);
        var second = TestImageFactory.WritePngToTempFile(120, 90);
        try
        {
            await _vm.AddFilesAsync([first]);
            await PushAnUndoableEditAsync();
            var notified = 0;
            _messenger.Register<object, CopyLibraryChangedMessage>(this, (_, _) => notified++);

            await _vm.AddFilesAsync([second]);

            _history.CanUndo.Should().BeFalse("新規アセットは Undo 対象外で、 履歴の参照整合性のため消す");
            notified.Should().Be(1);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public async Task Mixed_Import_Lists_Each_Failed_File_With_Its_Reason_And_Counts_All_Three_Outcomes()
    {
        var good = TestImageFactory.WritePngToTempFile(100, 100);
        var bogus = Path.Combine(Path.GetTempPath(), $"viewgrid-bogus-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(bogus, "not an image");
        try
        {
            await _vm.AddFilesAsync([good]);

            await _vm.AddFilesAsync([good, bogus]); // 重複 1 + 失敗 1

            var lines = _vm.StatusMessage!.Split('\n');
            lines[0].Should().Contain("Status_AssetDuplicatedFmt(1)").And.Contain("Status_AssetImportFailedFmt(1)");
            lines.Should().Contain(l => l.Contains("Status_AssetImportFailureDetailFmt(" + Path.GetFileName(bogus) + ","),
                "失敗したファイル名と理由がログを見なくても分かる");
            _vm.HasFailedImports.Should().BeTrue();
        }
        finally
        {
            File.Delete(good);
            File.Delete(bogus);
        }
    }

    [Fact]
    public async Task Retry_Failed_Imports_Imports_Only_The_Failed_Files_After_They_Are_Fixed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewgrid-retry-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(path, "not an image yet");
        try
        {
            await _vm.AddFilesAsync([path]);
            _vm.Assets.Should().BeEmpty();
            _vm.RetryFailedImportsCommand.CanExecute(null).Should().BeTrue();

            await File.WriteAllBytesAsync(path, TestImageFactory.CreatePng(64, 64)); // 直した
            await _vm.RetryFailedImportsAsync();

            _vm.Assets.Should().HaveCount(1);
            _vm.HasFailedImports.Should().BeFalse("成功したので再試行の対象が残らない");
            _vm.RetryFailedImportsCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Failure_List_Is_Capped_With_A_More_Line()
    {
        var bogus = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var p = Path.Combine(Path.GetTempPath(), $"viewgrid-many-{i}-{Guid.NewGuid():N}.png");
            await File.WriteAllTextAsync(p, "x");
            bogus.Add(p);
        }
        try
        {
            await _vm.AddFilesAsync(bogus);

            var lines = _vm.StatusMessage!.Split('\n');
            lines.Count(l => l.Contains("Status_AssetImportFailureDetailFmt")).Should().Be(5);
            lines.Last().Should().Contain("Status_AssetImportFailureMoreFmt(2)");
        }
        finally
        {
            foreach (var p in bogus) File.Delete(p);
        }
    }
}
