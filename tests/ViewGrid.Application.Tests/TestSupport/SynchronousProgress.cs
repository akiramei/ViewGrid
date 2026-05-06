namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// テスト中の順序保証用に <see cref="IProgress{T}.Report"/> を呼び出しスレッドで同期的に実行する
/// <see cref="IProgress{T}"/> 実装。 既定の <see cref="Progress{T}"/> は SynchronizationContext が
/// 無い環境では callback を ThreadPool で非同期実行するため、 await 直後の assertion で
/// 最後の Report を取りこぼすことがある。 同期実行で race を排除する。
/// </summary>
public sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
