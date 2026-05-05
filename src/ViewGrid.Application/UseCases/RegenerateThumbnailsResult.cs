namespace ViewGrid.Application.UseCases;

/// <summary>
/// <see cref="RegenerateThumbnailsUseCase"/> の最終結果。
/// </summary>
/// <param name="Total">処理対象の全アセット数。</param>
/// <param name="Successful">サムネ再生成に成功したアセット数。</param>
/// <param name="Skipped">元アセットファイルが見つからない等でスキップしたアセット数。</param>
/// <param name="Failed">削除 / 生成で失敗したアセット数 (ログに詳細)。</param>
/// <param name="Cancelled">途中キャンセルされたかどうか。</param>
public sealed record RegenerateThumbnailsResult(
    int Total,
    int Successful,
    int Skipped,
    int Failed,
    bool Cancelled);
