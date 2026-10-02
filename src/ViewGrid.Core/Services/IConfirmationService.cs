namespace ViewGrid.Core.Services;

/// <summary>未保存の編集があるまま別の対象へ移る (または終了する) ときの、利用者の選択。</summary>
public enum UnsavedChoice
{
    /// <summary>編集を保存してから先へ進む。</summary>
    Save,

    /// <summary>編集を破棄して先へ進む。</summary>
    Discard,

    /// <summary>先へ進むのをやめ、編集中の対象へ戻る (編集は残る)。</summary>
    Cancel,
}

/// <summary>
/// 破壊的操作 (削除など) や、未保存の編集が失われかねない操作の実行前に、 影響を示してユーザーへ確認する
/// UI の抽象。 ViewModel が「確認 → 実行」の流れを持てるようにし (取消時に何も変えないことをテストできる)、
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

    /// <summary>
    /// 未保存の編集の扱いを 3 択 (保存 / 破棄 / 戻る) で尋ねる。 ダイアログを閉じた場合は
    /// <see cref="UnsavedChoice.Cancel"/> (編集を失わない側) として扱う。
    /// </summary>
    Task<UnsavedChoice> AskUnsavedChangesAsync(
        string title, string message,
        string saveLabel, string discardLabel, string cancelLabel,
        CancellationToken ct = default);
}
