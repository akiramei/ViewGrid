using ViewGrid.Core.Services;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// テスト用の <see cref="IConfirmationService"/>。 回答を設定どおりに返し、問い合わせの内容を記録する。
/// 既定は削除などの確認 = 「実行する」 (<see cref="Answer"/>=true)、 未保存の編集の 3 択 = 「破棄」
/// (<see cref="UnsavedAnswer"/>=Discard。 手動保存モードで切り替えると編集が捨てられる従来の挙動と同じ結果になる)。
/// </summary>
internal sealed class AutoConfirmationService : IConfirmationService
{
    /// <summary>確認 (削除など) への回答。 <c>false</c> にすると取り消し (キャンセル) の経路を試せる。</summary>
    public bool Answer { get; set; } = true;

    /// <summary>未保存の編集の 3 択 (保存 / 破棄 / 戻る) への回答。</summary>
    public UnsavedChoice UnsavedAnswer { get; set; } = UnsavedChoice.Discard;

    /// <summary>これまでの確認要求 (古い順)。</summary>
    public List<(string Title, string Message, string ConfirmLabel)> Requests { get; } = [];

    /// <summary>これまでの未保存の編集に関する問い合わせ (古い順)。</summary>
    public List<(string Title, string Message)> UnsavedRequests { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct = default)
    {
        Requests.Add((title, message, confirmLabel));
        return Task.FromResult(Answer);
    }

    public Task<UnsavedChoice> AskUnsavedChangesAsync(
        string title, string message, string saveLabel, string discardLabel, string cancelLabel,
        CancellationToken ct = default)
    {
        UnsavedRequests.Add((title, message));
        return Task.FromResult(UnsavedAnswer);
    }
}
