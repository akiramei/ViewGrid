using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class CreateLogicalCopyUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private CreateLogicalCopyUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new CreateLogicalCopyUseCase(_fx.AssetRepository, _fx.CopyRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Creates_Copy_With_Default_Characteristics_When_Transform_Omitted()
    {
        var asset = await _fx.SeedAssetAsync();

        var result = await _useCase.ExecuteAsync(asset.Id);

        result.IsError.Should().BeFalse();
        var copy = result.Value;
        copy.AssetId.Should().Be(asset.Id);
        copy.Transform.Should().Be(ImageTransform.Identity);
        copy.ScalingMode.Should().Be(ScalingMode.UniformContain);
        copy.Alignment.Should().Be(Alignment.Center);
        copy.OccupySize.Should().Be(OccupySize.OneByOne);
        copy.CopyName.Should().BeNull();
    }

    [Fact]
    public async Task Applies_Given_Transform_And_CopyName()
    {
        var asset = await _fx.SeedAssetAsync();
        var transform = new ImageTransform(Rotation.Cw90, FlipX: true, FlipY: false);

        var result = await _useCase.ExecuteAsync(asset.Id, copyName: "rotated-flipped", transform: transform);

        result.IsError.Should().BeFalse();
        result.Value.CopyName.Should().Be("rotated-flipped");
        result.Value.Transform.Should().Be(transform);
    }

    [Fact]
    public async Task Creates_Multiple_Distinct_Copies_For_Same_Asset()
    {
        var asset = await _fx.SeedAssetAsync();

        var a = await _useCase.ExecuteAsync(asset.Id, copyName: "a");
        var b = await _useCase.ExecuteAsync(asset.Id, copyName: "b");

        a.IsError.Should().BeFalse();
        b.IsError.Should().BeFalse();
        a.Value.Id.Should().NotBe(b.Value.Id);

        var all = await _fx.CopyRepository.FindByAssetIdAsync(asset.Id);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Asset()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid());

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }
}
