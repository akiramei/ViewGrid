using System.IO;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class DeleteImageAssetUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private DeleteImageAssetUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new DeleteImageAssetUseCase(_fx.AssetRepository, _fx.Storage, _fx.Thumbnails);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Removes_Asset_From_Database()
    {
        var asset = await _fx.SeedAssetAsync();

        var result = await _useCase.ExecuteAsync(asset.Id);

        result.IsError.Should().BeFalse();
        var reloaded = await _fx.AssetRepository.FindByIdAsync(asset.Id);
        reloaded.Should().BeNull();
    }

    [Fact]
    public async Task Removes_Physical_Asset_File()
    {
        var asset = await _fx.SeedAssetAsync();
        var absolute = _fx.Storage.ResolveAbsolutePath(asset.StoredRelativePath);
        File.Exists(absolute).Should().BeTrue("precondition: seeded file exists");

        await _useCase.ExecuteAsync(asset.Id);

        File.Exists(absolute).Should().BeFalse();
    }

    [Fact]
    public async Task Removes_Thumbnail_When_Previously_Generated()
    {
        var asset = await _fx.SeedAssetAsync(width: 300, height: 300);
        var thumbResult = await _fx.Thumbnails.GenerateAsync(asset.StoredRelativePath, asset.FileHash);
        thumbResult.IsError.Should().BeFalse();
        _fx.Thumbnails.TryResolveAbsolutePath(asset.FileHash).Should().NotBeNull();

        await _useCase.ExecuteAsync(asset.Id);

        _fx.Thumbnails.TryResolveAbsolutePath(asset.FileHash).Should().BeNull();
    }

    [Fact]
    public async Task Cascades_To_Related_Copies_In_Database()
    {
        var asset = await _fx.SeedAssetAsync();
        await _fx.SeedCopyAsync(asset.Id);
        await _fx.SeedCopyAsync(asset.Id, copyName: "rotated");

        await _useCase.ExecuteAsync(asset.Id);

        var remaining = await _fx.CopyRepository.FindByAssetIdAsync(asset.Id);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_NotFound_When_Asset_Missing()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid());

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Succeeds_When_No_Thumbnail_Was_Ever_Generated()
    {
        var asset = await _fx.SeedAssetAsync();
        _fx.Thumbnails.TryResolveAbsolutePath(asset.FileHash).Should().BeNull();

        var result = await _useCase.ExecuteAsync(asset.Id);

        result.IsError.Should().BeFalse();
    }
}
