using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdateImageCopyUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdateImageCopyUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdateImageCopyUseCase(_fx.CopyRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Applies_Only_Provided_Fields_And_Preserves_Others()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id, copyName: "initial");

        var changes = new UpdateImageCopyChanges
        {
            ScalingMode = ScalingMode.UniformContainShrinkOnly,
            Alignment = new Alignment(AnchorX.Right, AnchorY.Bottom),
        };

        var result = await _useCase.ExecuteAsync(original.Id, changes);

        result.IsError.Should().BeFalse();
        var updated = result.Value;
        updated.Id.Should().Be(original.Id);
        updated.AssetId.Should().Be(original.AssetId);
        updated.ScalingMode.Should().Be(ScalingMode.UniformContainShrinkOnly);
        updated.Alignment.X.Should().Be(AnchorX.Right);
        updated.Alignment.Y.Should().Be(AnchorY.Bottom);

        // 未指定のフィールドは据え置き
        updated.CopyName.Should().Be("initial");
        updated.TrimmingAnchor.Should().Be(TrimmingAnchor.Center);
        updated.Transform.Should().Be(ImageTransform.Identity);
        updated.OccupySize.Should().Be(OccupySize.OneByOne);
    }

    [Fact]
    public async Task Updates_UpdatedAt_And_Preserves_CreatedAt()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id);

        // DateTimeOffset.UtcNow の Windows での分解能は ~15.6ms のため、安全側で 50ms 待機
        await Task.Delay(50);

        var result = await _useCase.ExecuteAsync(
            original.Id,
            new UpdateImageCopyChanges { CopyName = "renamed" });

        result.IsError.Should().BeFalse();
        result.Value.CreatedAt.Should().Be(original.CreatedAt);
        result.Value.UpdatedAt.Should().BeAfter(original.UpdatedAt);
    }

    [Fact]
    public async Task Persists_Changes_So_Subsequent_Reads_See_Them()
    {
        var asset = await _fx.SeedAssetAsync();
        var original = await _fx.SeedCopyAsync(asset.Id);

        await _useCase.ExecuteAsync(
            original.Id,
            new UpdateImageCopyChanges
            {
                Transform = new ImageTransform(Rotation.Cw180, FlipX: false, FlipY: true),
                OccupySize = new OccupySize(2, 3),
            });

        var reloaded = await _fx.CopyRepository.FindByIdAsync(original.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Transform.Rotation.Should().Be(Rotation.Cw180);
        reloaded.Transform.FlipY.Should().BeTrue();
        reloaded.OccupySize.Width.Should().Be(2);
        reloaded.OccupySize.Height.Should().Be(3);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Copy()
    {
        var result = await _useCase.ExecuteAsync(
            Guid.NewGuid(),
            new UpdateImageCopyChanges { CopyName = "ghost" });

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }
}
