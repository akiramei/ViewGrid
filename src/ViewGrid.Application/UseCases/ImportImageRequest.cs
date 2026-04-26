using ViewGrid.Core.Entities;

namespace ViewGrid.Application.UseCases;

public sealed record ImportImageRequest
{
    /// <summary>取り込み元ファイルの絶対パス。</summary>
    public required string SourcePath { get; init; }

    /// <summary>画像の取り込み元種別（ファイル・URL・クリップボード等）。</summary>
    public ImageSource SourceType { get; init; } = ImageSource.File;
}
