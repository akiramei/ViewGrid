using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.UseCases;

public sealed record ExportGridResult(string Path, long FileSizeBytes);

/// <summary>
/// グリッドを合成して指定パスに PNG として書き出す。レンダリング自体は
/// <see cref="RenderGridUseCase"/> に委譲し、本ユースケースはファイル I/O を担う。
/// </summary>
public sealed class ExportGridUseCase(RenderGridUseCase render)
{
    public Task<ErrorOr<ExportGridResult>> ExecuteAsync(
        Guid gridId,
        string outputPath,
        TrimMode trimMode = TrimMode.None,
        CancellationToken ct = default)
        => ExecuteAsync(gridId, outputPath, new RenderOptions(trimMode), ct);

    public async Task<ErrorOr<ExportGridResult>> ExecuteAsync(
        Guid gridId,
        string outputPath,
        RenderOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(outputPath))
            return Error.Validation("Export.InvalidPath", "出力パスが空です。");

        var rendered = await render.ExecuteAsync(gridId, options, ct);
        if (rendered.IsError)
            return rendered.Errors;

        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(outputPath, rendered.Value, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error.Failure("Export.AccessDenied", ex.Message);
        }
        catch (IOException ex)
        {
            return Error.Failure("Export.IoError", ex.Message);
        }

        return new ExportGridResult(outputPath, rendered.Value.LongLength);
    }
}
