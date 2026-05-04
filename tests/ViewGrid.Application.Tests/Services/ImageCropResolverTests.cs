using FluentAssertions;
using NSubstitute;
using ViewGrid.Application.Services;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.Tests.Services;

/// <summary>
/// <see cref="ImageCropResolver"/> の優先順位ロジックの単体テスト。
/// ManualCrop > AutoCrop > null の順で解決され、ManualCrop 設定時には AutoCrop 経路（I/O 走査）が
/// 一切呼ばれないことを検証する。
/// </summary>
public sealed class ImageCropResolverTests
{
    private readonly IAutoCropBboxResolver _autoCropResolver = Substitute.For<IAutoCropBboxResolver>();
    private readonly IImageStorage _storage = Substitute.For<IImageStorage>();
    private readonly ImageCropResolver _sut;

    public ImageCropResolverTests()
    {
        _sut = new ImageCropResolver(_autoCropResolver, _storage);
        _storage.ResolveAbsolutePath(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
    }

    private static ImageCopy MakeCopy(
        AutoCropSettings? autoCrop = null,
        ManualCropFraction? manualCrop = null)
    {
        return new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Transform = ImageTransform.Identity,
            ScalingMode = ScalingMode.UniformContain,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            AutoCrop = autoCrop,
            ManualCrop = manualCrop,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ImageAsset MakeAsset() => new()
    {
        Id = Guid.NewGuid(),
        SourceType = ImageSource.File,
        StoredRelativePath = "rel/path.png",
        Size = new PixelSize(800, 600),
        FileHash = "h",
        FileSizeBytes = 1024,
        MimeType = "image/png",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task ResolveAsync_Returns_Null_When_Both_Crops_Are_Off()
    {
        var copy = MakeCopy(autoCrop: null, manualCrop: null);

        var result = await _sut.ResolveAsync(copy, MakeAsset());

        result.Should().BeNull();
        await _autoCropResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ResolveAsync_Manual_Wins_Over_Auto()
    {
        // 両方設定。Manual が優先される。
        var manual = new ManualCropFraction(0.1, 0.2, 0.3, 0.4);
        var copy = MakeCopy(autoCrop: AutoCropSettings.White, manualCrop: manual);

        var result = await _sut.ResolveAsync(copy, MakeAsset());

        result.Should().Be(new CropFraction(0.1, 0.2, 0.3, 0.4));
        await _autoCropResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ResolveAsync_Manual_Full_Returns_Null()
    {
        // Manual は設定されているが Full（クロップ無効）→ AutoCrop も無視して null
        var copy = MakeCopy(autoCrop: AutoCropSettings.White, manualCrop: ManualCropFraction.Full);

        var result = await _sut.ResolveAsync(copy, MakeAsset());

        result.Should().BeNull();
        await _autoCropResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ResolveAsync_Falls_Back_To_AutoCrop_When_Manual_Is_Null()
    {
        var asset = MakeAsset();
        var copy = MakeCopy(autoCrop: AutoCropSettings.White, manualCrop: null);
        _autoCropResolver
            .ResolveAsync(asset.Id, Arg.Any<string>(), AutoCropSettings.White, Arg.Any<CancellationToken>())
            .Returns(new AutoCropFraction(0.05, 0.05, 0.9, 0.9));

        var result = await _sut.ResolveAsync(copy, asset);

        result.Should().Be(new CropFraction(0.05, 0.05, 0.9, 0.9));
    }

    [Fact]
    public async Task ResolveAsync_Returns_Null_When_AutoCrop_Resolver_Returns_Null()
    {
        var asset = MakeAsset();
        var copy = MakeCopy(autoCrop: AutoCropSettings.Black, manualCrop: null);
        _autoCropResolver
            .ResolveAsync(asset.Id, Arg.Any<string>(), AutoCropSettings.Black, Arg.Any<CancellationToken>())
            .Returns((AutoCropFraction?)null);

        var result = await _sut.ResolveAsync(copy, asset);

        result.Should().BeNull();
    }
}
