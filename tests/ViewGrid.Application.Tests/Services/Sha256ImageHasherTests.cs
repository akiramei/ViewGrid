using System.IO;
using System.Text;
using FluentAssertions;
using ViewGrid.Infrastructure.Services;
using Xunit;

namespace ViewGrid.Application.Tests.Services;

public sealed class Sha256ImageHasherTests
{
    [Fact]
    public async Task Computes_Known_Hash_For_Empty_Stream()
    {
        var hasher = new Sha256ImageHasher();
        using var stream = new MemoryStream();

        var hash = await hasher.ComputeHashAsync(stream);

        // 空ストリームの SHA-256
        hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task Computes_Known_Hash_For_Known_Content()
    {
        var hasher = new Sha256ImageHasher();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var hash = await hasher.ComputeHashAsync(stream);

        // "hello" の SHA-256
        hash.Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
    }

    [Fact]
    public async Task Returns_Lowercase_Hex_Of_Length_64()
    {
        var hasher = new Sha256ImageHasher();
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);

        var hash = await hasher.ComputeHashAsync(stream);

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
