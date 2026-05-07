using FluentAssertions;
using ViewGrid.Application.AutoSave;

namespace ViewGrid.Application.Tests.AutoSave;

public sealed class SaveCoordinatorTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 設定 OFF (isEnabled=false) のときは <see cref="SaveCoordinator.NotifyEdited"/> 連打しても
    /// debounce 経過後 saveAction は走らない。
    /// </summary>
    [Fact]
    public async Task NotifyEdited_WhenDisabled_DoesNotInvokeSaveAction()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => false,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            });

        coord.NotifyEdited();
        coord.NotifyEdited();
        await Task.Delay(300);

        calls.Should().Be(0);
    }

    /// <summary>
    /// dirty=false のときは Schedule されない (Coordinator 内 gate)。
    /// </summary>
    [Fact]
    public async Task NotifyEdited_WhenNotDirty_DoesNotInvokeSaveAction()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => false,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            });

        coord.NotifyEdited();
        await Task.Delay(300);

        calls.Should().Be(0);
    }

    /// <summary>
    /// 通常経路: 設定 ON + dirty + 新 signature → debounce 経過後 saveAction が 1 回走る。
    /// </summary>
    [Fact]
    public async Task NotifyEdited_WhenEnabledAndDirty_InvokesSaveActionOnce()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            });

        coord.NotifyEdited();
        await Task.Delay(300);

        calls.Should().Be(1);
    }

    /// <summary>
    /// saveAction が false (失敗) を返したあと、 同じ signature で <see cref="SaveCoordinator.NotifyEdited"/>
    /// しても再 Schedule されない (Validation 失敗ループ防止)。
    /// </summary>
    [Fact]
    public async Task NotifyEdited_AfterFailure_SameSignature_IsSuppressed()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig-A",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(false);  // 失敗
            });

        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(1);

        // 同 signature で再通知 → Coordinator 内で抑止される
        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(1);
    }

    /// <summary>
    /// 失敗後に signature が変わると (=ユーザーが編集を続けると) 再 Schedule され、 retry が走る。
    /// </summary>
    [Fact]
    public async Task NotifyEdited_AfterFailure_NewSignature_IsRetried()
    {
        var calls = 0;
        var sig = "sig-A";
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => sig,
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(false);
            });

        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(1);

        // signature 変化 = ユーザーが編集 → retry が走る
        sig = "sig-B";
        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(2);
    }

    /// <summary>
    /// <see cref="SaveCoordinator.ResetFailureGuard"/> で失敗 signature をクリアすると、
    /// 同 signature でも retry が走る (編集対象切替時用)。
    /// </summary>
    [Fact]
    public async Task ResetFailureGuard_AllowsRetryWithSameSignature()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(false);
            });

        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(1);

        coord.ResetFailureGuard();
        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(2);
    }

    /// <summary>
    /// <see cref="SaveCoordinator.FlushAsync"/> で保留中タイマーを即実行 + 完了待ちできる。
    /// </summary>
    [Fact]
    public async Task FlushAsync_ExecutesPendingScheduleImmediately()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            });

        coord.NotifyEdited();
        await coord.FlushAsync();

        calls.Should().Be(1);
    }

    /// <summary>
    /// <see cref="SaveCoordinator.Cancel"/> で保留中タイマーを破棄すると、 saveAction は走らない。
    /// </summary>
    [Fact]
    public async Task Cancel_DropsPendingSchedule()
    {
        var calls = 0;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            });

        coord.NotifyEdited();
        await Task.Delay(30);
        coord.Cancel();
        await Task.Delay(300);

        calls.Should().Be(0);
    }

    /// <summary>
    /// Dispose 後の <see cref="SaveCoordinator.NotifyEdited"/> / <see cref="SaveCoordinator.Cancel"/>
    /// は静かに無視される (進行中の保存は完遂)。
    /// </summary>
    [Fact]
    public async Task Dispose_IsIdempotent_AndIgnoresFurtherCalls()
    {
        var calls = 0;
        var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            });

        coord.Dispose();
        coord.Dispose();  // 二重 Dispose も throw しない

        coord.NotifyEdited();
        coord.Cancel();
        coord.ResetFailureGuard();
        await Task.Delay(300);

        calls.Should().Be(0);
    }

    /// <summary>
    /// 失敗後に成功すると、 失敗 signature がクリアされて再 Schedule できる状態に戻る。
    /// </summary>
    [Fact]
    public async Task SuccessAfterFailure_ClearsFailureSignature()
    {
        var calls = 0;
        var shouldFail = true;
        using var coord = new SaveCoordinator(
            Debounce,
            isEnabled: () => true,
            isDirty: () => true,
            signatureProvider: () => "sig",
            saveAction: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(!shouldFail);
            });

        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(1);

        // 同 signature で再通知 → 抑止される
        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(1);

        // 別 signature でなくても、 ResetFailureGuard 後に成功すれば次回以降は同 signature で動く
        shouldFail = false;
        coord.ResetFailureGuard();
        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(2);

        // 成功後の同 signature 通知も走る (失敗 guard はクリア済み)
        coord.NotifyEdited();
        await Task.Delay(300);
        calls.Should().Be(3);
    }
}
