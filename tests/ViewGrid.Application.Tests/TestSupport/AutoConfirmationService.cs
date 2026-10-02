using ViewGrid.Core.Services;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// テスト用の <see cref="IConfirmationService"/>。 <see cref="Answer"/> をそのまま返し、
/// 問い合わせの内容 (タイトル・本文・ボタン名) を記録する。 既定は「実行する」 (true)。
/// </summary>
internal sealed class AutoConfirmationService : IConfirmationService
{
    /// <summary>確認への回答。 <c>false</c> にすると取り消し (キャンセル) の経路を試せる。</summary>
    public bool Answer { get; set; } = true;

    /// <summary>これまでの確認要求 (古い順)。</summary>
    public List<(string Title, string Message, string ConfirmLabel)> Requests { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct = default)
    {
        Requests.Add((title, message, confirmLabel));
        return Task.FromResult(Answer);
    }
}
