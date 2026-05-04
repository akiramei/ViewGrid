using System.Globalization;
using System.Security.Cryptography;
using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Services;

internal sealed class Sha256ImageHasher : IImageHasher
{
    public async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
