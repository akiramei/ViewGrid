using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Services;

internal sealed class FileSystemImageStorage(StorageOptions options) : IImageStorage
{
    private readonly StorageOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string BuildRelativePath(string fileHash, string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileHash);
        ArgumentException.ThrowIfNullOrEmpty(extension);
        if (fileHash.Length < 2)
            throw new ArgumentException("fileHash は最低 2 文字必要です。", nameof(fileHash));

        var shard = fileHash[..2];
        var ext = extension.StartsWith('.') ? extension : '.' + extension;

        // 相対パスはスラッシュで統一する（DB 永続化時の可読性と OS 間の互換性確保）
        return $"{_options.AssetsSubdirectory}/{shard}/{fileHash}{ext}";
    }

    public bool Exists(string relativePath) => File.Exists(ResolveAbsolutePath(relativePath));

    public async Task SaveAsync(Stream source, string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var absolute = ResolveAbsolutePath(relativePath);
        var directory = Path.GetDirectoryName(absolute)
            ?? throw new InvalidOperationException("保存先ディレクトリを決定できません。");

        Directory.CreateDirectory(directory);

        if (File.Exists(absolute))
            return;

        await using var destination = new FileStream(
            absolute,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        await source.CopyToAsync(destination, ct);
    }

    public string ResolveAbsolutePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_options.DataDirectory, normalized);
    }

    public void Delete(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return;

        var absolute = ResolveAbsolutePath(relativePath);
        if (File.Exists(absolute))
            File.Delete(absolute);
    }
}
