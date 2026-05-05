namespace ViewGrid.Application.UseCases;

/// <summary>
/// <see cref="RegenerateThumbnailsUseCase"/> の進捗報告用イベントデータ。
/// 進捗ダイアログ ViewModel が <see cref="IProgress{T}"/> 経由で受け取る。
/// </summary>
public sealed record ThumbnailRegenProgress(
    int Completed,
    int Total,
    string CurrentAssetName,
    int Successful,
    int Skipped,
    int Failed);
