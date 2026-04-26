using ViewGrid.Core.Entities;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 画像取り込みの結果。
/// </summary>
public sealed record ImportImageResult(ImageAsset Asset, ImageCopy DefaultCopy, bool WasDuplicate);
