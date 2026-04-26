using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Services;

/// <summary>
/// 画像ファイルから取り出したメタデータ。
/// </summary>
public readonly record struct ImageProbe(PixelSize Size, string MimeType);
