namespace ViewGrid.Core.Services;

/// <summary>
/// 破壊的操作 (削除など) の実行前に、 影響を示してユーザーへ確認する UI の抽象。
/// ViewModel が「確認 → 実行」の流れを持てるようにし (取消時に何も変えないことをテストできる)、
/// 実際のダイアログ表示は Presentation 層が担う。
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// 確認を求める。 ユーザーが実行を選んだら <c>true</c>、 取り消し (キャンセル・閉じる) なら <c>false</c>。
    /// </summary>
    /// <param name="title">ダイアログのタイトル。</param>
    /// <param name="message">対象と影響 (関連する件数・元に戻せないこと) を示す本文。</param>
    /// <param name="confirmLabel">実行ボタンのラベル (例: 「削除」)。</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct = default);
}
