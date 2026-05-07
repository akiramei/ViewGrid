using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

/// <summary>
/// <see cref="WorkspaceLock"/> のロック取得 / 競合 / 解放 / 別ワークスペース並行を検証する。
/// 同一プロセス内でも <see cref="FileShare.Read"/> ロックは効くため、 サブプロセスを起こさずに検証可能。
/// </summary>
public sealed class WorkspaceLockTests : IAsyncLifetime
{
    private DirectoryInfo _root = null!;
    private string _workspaceA = null!;
    private string _workspaceB = null!;

    public Task InitializeAsync()
    {
        _root = TestImageFactory.CreateTempDirectory();
        _workspaceA = Path.Combine(_root.FullName, "ws-a");
        _workspaceB = Path.Combine(_root.FullName, "ws-b");
        Directory.CreateDirectory(_workspaceA);
        Directory.CreateDirectory(_workspaceB);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public void TryAcquire_OnFreshDirectory_Succeeds()
    {
        var result = WorkspaceLock.TryAcquire(_workspaceA);

        try
        {
            result.Acquired.Should().BeTrue();
            result.Lock.Should().NotBeNull();
            result.OwnerProcessId.Should().BeNull();
            File.Exists(Path.Combine(_workspaceA, ".lock")).Should().BeTrue();
        }
        finally
        {
            result.Lock?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_WhileHeld_Fails_AndReportsOwnerPid()
    {
        var first = WorkspaceLock.TryAcquire(_workspaceA);
        try
        {
            first.Acquired.Should().BeTrue();

            var second = WorkspaceLock.TryAcquire(_workspaceA);

            second.Acquired.Should().BeFalse();
            second.Lock.Should().BeNull();
            second.OwnerProcessId.Should().Be(Environment.ProcessId);
        }
        finally
        {
            first.Lock?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_AfterDispose_Succeeds()
    {
        var first = WorkspaceLock.TryAcquire(_workspaceA);
        first.Acquired.Should().BeTrue();
        first.Lock!.Dispose();

        var second = WorkspaceLock.TryAcquire(_workspaceA);
        try
        {
            second.Acquired.Should().BeTrue();
        }
        finally
        {
            second.Lock?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_DifferentWorkspaces_BothSucceed()
    {
        var lockA = WorkspaceLock.TryAcquire(_workspaceA);
        var lockB = WorkspaceLock.TryAcquire(_workspaceB);

        try
        {
            lockA.Acquired.Should().BeTrue();
            lockB.Acquired.Should().BeTrue();
        }
        finally
        {
            lockA.Lock?.Dispose();
            lockB.Lock?.Dispose();
        }
    }

    /// <summary>
    /// Dispose 後にロックファイル自体は残置される (Linux での unlink レース回避目的)。
    /// 残ったファイルがあっても次回 Acquire は <see cref="FileMode.OpenOrCreate"/> で上書きできる。
    /// </summary>
    [Fact]
    public void Dispose_LeavesLockFile_ButAllowsReacquire()
    {
        var first = WorkspaceLock.TryAcquire(_workspaceA);
        first.Acquired.Should().BeTrue();
        first.Lock!.Dispose();

        var lockPath = Path.Combine(_workspaceA, ".lock");
        File.Exists(lockPath).Should().BeTrue();

        var second = WorkspaceLock.TryAcquire(_workspaceA);
        try
        {
            second.Acquired.Should().BeTrue();
        }
        finally
        {
            second.Lock?.Dispose();
        }
    }

    /// <summary>
    /// 既存 <c>.lock</c> に不正な PID 文字列が書かれていても、 取得自体は成功して上書きする。
    /// 失敗時の OwnerProcessId は best-effort なので、 不正値はパースエラーで <c>null</c> 扱い。
    /// </summary>
    [Fact]
    public void TryAcquire_OverwritesGarbagePidContent()
    {
        var lockPath = Path.Combine(_workspaceA, ".lock");
        File.WriteAllText(lockPath, "not-a-number");

        var result = WorkspaceLock.TryAcquire(_workspaceA);
        try
        {
            result.Acquired.Should().BeTrue();
            result.OwnerProcessId.Should().BeNull();
        }
        finally
        {
            result.Lock?.Dispose();
        }
    }
}
