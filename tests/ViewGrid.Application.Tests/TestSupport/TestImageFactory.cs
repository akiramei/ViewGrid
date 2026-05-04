using SkiaSharp;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// SkiaSharp を使って単色 PNG / JPEG をその場で生成するテストヘルパー。
/// </summary>
internal static class TestImageFactory
{
    public static byte[] CreatePng(int width, int height, SKColor? fill = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(fill ?? SKColors.CornflowerBlue);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public static string WritePngToTempFile(int width, int height, string? extension = ".png")
    {
        var path = Path.Combine(Path.GetTempPath(), $"viewgrid-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, CreatePng(width, height));
        return path;
    }

    /// <summary>
    /// テスト専用の一時ディレクトリを作成する（呼び出し側が破棄する）。
    /// </summary>
    public static DirectoryInfo CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"viewgrid-test-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(dir);
    }
}
