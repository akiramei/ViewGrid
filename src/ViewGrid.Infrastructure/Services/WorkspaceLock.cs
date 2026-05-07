using System.Globalization;
using System.Text;

namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// 1 ワークスペース 1 プロセスを強制するためのファイルベースロック。
/// <c>{workspaceDirectory}\.lock</c> を <see cref="FileShare.Read"/> で開きっぱなしに保持し、
/// 他プロセスが書き込みアクセスで開こうとすると OS が IOException を返す。
/// プロセス死亡時には OS がハンドルを解放するため、 orphan ロックは起きにくい。
/// </summary>
/// <remarks>
/// 別ワークスペース (別ディレクトリ) の並行起動は許容する。
/// 同一ワークスペースでの二重起動だけを防ぐ目的。
/// </remarks>
public sealed class WorkspaceLock : IDisposable
{
    private const string LockFileName = ".lock";

    private FileStream? _stream;
    private readonly string _lockPath;

    private WorkspaceLock(string lockPath, FileStream stream)
    {
        _lockPath = lockPath;
        _stream = stream;
    }

    /// <summary>
    /// ワークスペースのロック取得を試みる。 成功時は <see cref="WorkspaceLockAcquireResult.Lock"/> に
    /// インスタンスが入り、 アプリ終了まで保持する。 失敗時は既存占有プロセスの PID
    /// (<see cref="WorkspaceLockAcquireResult.OwnerProcessId"/>) を best-effort で返す。
    /// </summary>
    /// <param name="workspaceDirectory">対象ワークスペースのディレクトリ。 既に存在している必要がある。</param>
    public static WorkspaceLockAcquireResult TryAcquire(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceDirectory);

        var lockPath = Path.Combine(workspaceDirectory, LockFileName);

        // 競合時に既存の PID を返すため、 ロック取得失敗時は読み取りを試みる。
        // 成功時は自身の PID で上書きするため、 ここでの値は捨てる。
        int? existingPid = TryReadOwnerPid(lockPath);

        FileStream fs;
        try
        {
            fs = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);
        }
        catch (IOException)
        {
            return new WorkspaceLockAcquireResult(null, existingPid);
        }
        catch (UnauthorizedAccessException)
        {
            return new WorkspaceLockAcquireResult(null, existingPid);
        }

        try
        {
            fs.SetLength(0);
            var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            var bytes = Encoding.UTF8.GetBytes(pid);
            fs.Write(bytes);
            fs.Flush();
        }
        catch
        {
            fs.Dispose();
            throw;
        }

        return new WorkspaceLockAcquireResult(new WorkspaceLock(lockPath, fs), null);
    }

    /// <summary>
    /// ロックを保持したまま、 占有 PID を best-effort で読み出す。 ロック保持側が
    /// <see cref="FileShare.Read"/> で開いているので、 読みは成功するが書き込み中の torn read で
    /// パース失敗する可能性はある (その場合は <c>null</c>)。
    /// </summary>
    private static int? TryReadOwnerPid(string lockPath)
    {
        if (!File.Exists(lockPath))
            return null;

        try
        {
            using var reader = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var sr = new StreamReader(reader, Encoding.UTF8);
            var content = sr.ReadToEnd().Trim();
            return int.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                ? pid
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_stream is null)
            return;

        _stream.Dispose();
        _stream = null;

        // ロックファイル本体は削除しない。 Linux では unlink がオープン中ハンドルと独立して動く
        // ため、 「Dispose 後の Delete」 と 「別プロセスの新規 Acquire」 が交錯すると、 削除直前に
        // 開かれた既存ハンドルと、 削除後に再生成されたファイル上の新規ハンドルが共存し、 短い
        // 二重保持窓が生まれる可能性がある。 次回 <see cref="TryAcquire(string)"/> は
        // <see cref="FileMode.OpenOrCreate"/> + <see cref="FileStream.SetLength(long)"/> で上書きするため、
        // 残置されていても無害。
    }
}

/// <summary>
/// <see cref="WorkspaceLock.TryAcquire(string)"/> の結果。
/// </summary>
/// <param name="Lock">取得成功時のロックインスタンス。 失敗時は <c>null</c>。</param>
/// <param name="OwnerProcessId">取得失敗時に best-effort で取得した既存占有プロセスの PID。</param>
public readonly record struct WorkspaceLockAcquireResult(WorkspaceLock? Lock, int? OwnerProcessId)
{
    /// <summary>取得成功なら <c>true</c>。</summary>
    public bool Acquired => Lock is not null;
}
