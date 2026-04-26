using System;

namespace ViewGrid.Core.Entities;

/// <summary>
/// 元画像アセット（取り込まれたオリジナル）。実体ファイルはファイルシステム上に保存し、
/// 本エンティティはそのメタデータのみを保持する。
/// </summary>
public sealed class ImageAsset
{
    public required Guid Id { get; init; }
    public required ImageSource SourceType { get; init; }

    /// <summary>元ファイル名（拡張子含む）。URL/Clipboard 由来では null の可能性あり。</summary>
    public string? OriginalFilename { get; init; }

    /// <summary>アプリ管理ストレージ内の相対パス（例: <c>assets/ab/abcd...png</c>）。</summary>
    public required string StoredRelativePath { get; init; }

    /// <summary>ピクセル単位のサイズ。</summary>
    public required PixelSize Size { get; init; }

    /// <summary>重複検出および StoredRelativePath 決定に用いる SHA-256（16 進小文字）。</summary>
    public required string FileHash { get; init; }

    /// <summary>ファイルサイズ（バイト）。</summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>MIME タイプ（例: <c>image/png</c>）。</summary>
    public required string MimeType { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
