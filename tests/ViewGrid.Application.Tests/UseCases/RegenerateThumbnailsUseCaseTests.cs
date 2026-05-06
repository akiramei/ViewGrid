using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class RegenerateThumbnailsUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private RegenerateThumbnailsUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new RegenerateThumbnailsUseCase(
            _fx.AssetRepository,
            _fx.Thumbnails,
            NullLogger<RegenerateThumbnailsUseCase>.Instance);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task ExecuteAsync_AllAssets_RegeneratesSuccessfully()
    {
        // 3 アセットを seed (実体ファイル付き) → 全件成功
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000001");
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000002");
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000003");

        var progressReports = new List<ThumbnailRegenProgress>();
        var progress = new Progress<ThumbnailRegenProgress>(progressReports.Add);

        var result = await _useCase.ExecuteAsync(progress);

        result.Total.Should().Be(3);
        result.Successful.Should().Be(3);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingSourceFile_CountsAsSkipped()
    {
        // 1 件は seed (実体あり) + 1 件は手動で「実体ファイル無し」 で DB に投入
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000010");
        var orphan = new ViewGrid.Core.Entities.ImageAsset
        {
            Id = Guid.NewGuid(),
            SourceType = ViewGrid.Core.Entities.ImageSource.File,
            OriginalFilename = "orphan.png",
            StoredRelativePath = "assets/no/no-such-file.png", // 実体なし
            Size = new ViewGrid.Core.Entities.PixelSize(100, 100),
            FileHash = "orphan000000000000000000000000000000000000000000000000000000ffff",
            FileSizeBytes = 123,
            MimeType = "image/png",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _fx.AssetRepository.AddAsync(orphan);

        var result = await _useCase.ExecuteAsync(progress: null);

        result.Total.Should().Be(2);
        result.Successful.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_ReturnsCancelledImmediately()
    {
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000020");
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000021");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _useCase.ExecuteAsync(progress: null, ct: cts.Token);

        result.Cancelled.Should().BeTrue();
        result.Successful.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ProgressReports_IncludeInitialAndPerAsset()
    {
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000030");
        await _fx.SeedAssetAsync(fileHash: "regen0000000000000000000000000000000000000000000000000000000031");

        var reports = new List<ThumbnailRegenProgress>();
        // SynchronizationContext を持たないテスト環境では Progress<T> の callback は
        // ThreadPool で実行されるため、 順序保証のため手動 IProgress 実装を使う
        var progress = new SynchronousProgress<ThumbnailRegenProgress>(reports.Add);

        await _useCase.ExecuteAsync(progress);

        // 初回 (Total を伝える) + 各アセット完了で 1 回ずつ → 計 3 回
        reports.Should().HaveCount(3);
        reports[0].Should().Match<ThumbnailRegenProgress>(p => p.Completed == 0 && p.Total == 2);
        reports[1].Should().Match<ThumbnailRegenProgress>(p => p.Completed == 1 && p.Total == 2);
        reports[2].Should().Match<ThumbnailRegenProgress>(p => p.Completed == 2 && p.Total == 2 && p.Successful == 2);
    }

}
