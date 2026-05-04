using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

public sealed class SkiaImageProberTests
{
    [Fact]
    public async Task Returns_Width_Height_And_PngMime_For_Png_Stream()
    {
        var prober = new SkiaImageProber();
        var png = TestImageFactory.CreatePng(120, 80);
        using var stream = new MemoryStream(png);

        var result = await prober.ProbeAsync(stream);

        result.IsError.Should().BeFalse();
        result.Value.Size.Width.Should().Be(120);
        result.Value.Size.Height.Should().Be(80);
        result.Value.MimeType.Should().Be("image/png");
    }

    [Fact]
    public async Task Returns_Validation_Error_For_Non_Image_Bytes()
    {
        var prober = new SkiaImageProber();
        using var stream = new MemoryStream("this is not an image"u8.ToArray());

        var result = await prober.ProbeAsync(stream);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }
}
