using FluentAssertions;
using ViewGrid.Application.AutoSave;

namespace ViewGrid.Application.Tests.AutoSave;

public sealed class AutoSaveDispatcherTests
{
    /// <summary>
    /// ベースの debounce 時間。 単体テストではミリ秒単位の差で揺れないよう、
    /// 余裕のある値 (100ms) と十分大きな wait (300ms) を使う。
    /// </summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task Schedule_AfterDebounce_RunsSaveActionOnce()
    {
        var calls = 0;
        using var dispatcher = new AutoSaveDispatcher(Debounce, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        dispatcher.Schedule();
        await Task.Delay(300);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Schedule_RepeatedQuickly_RunsSaveActionOnceAfterLastSchedule()
    {
        var calls = 0;
        using var dispatcher = new AutoSaveDispatcher(Debounce, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        dispatcher.Schedule();
        await Task.Delay(30);
        dispatcher.Schedule();
        await Task.Delay(30);
        dispatcher.Schedule();

        // ここから 100ms 以上待つと最後の Schedule の分だけが発火する。
        await Task.Delay(300);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_RemovesPendingSchedule()
    {
        var calls = 0;
        using var dispatcher = new AutoSaveDispatcher(Debounce, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        dispatcher.Schedule();
        await Task.Delay(30);
        dispatcher.Cancel();
        await Task.Delay(300);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task FlushNowAsync_WithPending_ExecutesImmediatelyAndAwaits()
    {
        var calls = 0;
        using var dispatcher = new AutoSaveDispatcher(Debounce, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        dispatcher.Schedule();
        await dispatcher.FlushNowAsync();

        // FlushNowAsync は完了を待つので、 戻った時点で 1 回実行済み。
        calls.Should().Be(1);

        // 追加で debounce 経過しても、 タイマーは奪われているので再実行されない。
        await Task.Delay(300);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task FlushNowAsync_WithoutPending_IsNoop()
    {
        var calls = 0;
        using var dispatcher = new AutoSaveDispatcher(Debounce, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        await dispatcher.FlushNowAsync();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task SaveAction_DoesNotRunInParallel_EvenWithConcurrentScheduleAndFlush()
    {
        var inFlight = 0;
        var maxInFlight = 0;
        var totalCalls = 0;
        var lockObj = new object();

        using var dispatcher = new AutoSaveDispatcher(Debounce, async ct =>
        {
            var current = Interlocked.Increment(ref inFlight);
            lock (lockObj)
            {
                if (current > maxInFlight) maxInFlight = current;
            }
            try
            {
                await Task.Delay(80, ct);
                Interlocked.Increment(ref totalCalls);
            }
            finally { Interlocked.Decrement(ref inFlight); }
        });

        // saveAction 実行中に Schedule + FlushNowAsync を被せる
        dispatcher.Schedule();
        await Task.Delay(120);  // 1 回目開始 (debounce 100ms 経過、 saveAction 80ms 実行中)
        dispatcher.Schedule();
        var flush = dispatcher.FlushNowAsync();
        await flush;
        await Task.Delay(50);  // 完了確認のための余裕

        // 同時実行されていないこと
        maxInFlight.Should().Be(1);
        // saveAction は最低 1 回、 多くて 2 回 (1 回目の自動 + flush)。 並列はしない。
        totalCalls.Should().BeGreaterOrEqualTo(1);
        totalCalls.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task Dispose_CancelsPendingSchedule_ButDoesNotThrowOnInFlight()
    {
        var calls = 0;
        var dispatcher = new AutoSaveDispatcher(Debounce, async ct =>
        {
            await Task.Delay(50, ct);
            Interlocked.Increment(ref calls);
        });

        dispatcher.Schedule();
        await Task.Delay(30);  // 保留中 (まだ saveAction は始まっていない)
        dispatcher.Dispose();
        await Task.Delay(300);

        // Dispose 後は schedule をキャンセルするので、 一度も発火しない。
        calls.Should().Be(0);

        // Dispose 後の Schedule / Cancel / FlushNowAsync は静かに無視される。
        dispatcher.Schedule();
        dispatcher.Cancel();
        await dispatcher.FlushNowAsync();
        await Task.Delay(300);
        calls.Should().Be(0);
    }
}
