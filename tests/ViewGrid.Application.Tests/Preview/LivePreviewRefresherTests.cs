using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.Preview;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Infrastructure;
using ViewGrid.Infrastructure.Persistence;

namespace ViewGrid.Application.Tests.Preview;

public sealed class LivePreviewRefresherTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(30);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public async Task A_Burst_Of_Requests_Renders_Once_With_The_Latest_State()
    {
        var renders = 0;
        var results = new List<byte[]?>();
        using var refresher = new LivePreviewRefresher(
            Short,
            _ => Task.FromResult<byte[]?>([(byte)Interlocked.Increment(ref renders)]),
            b => { lock (results) results.Add(b); });

        for (var i = 0; i < 20; i++) refresher.Request();

        (await WaitUntilAsync(() => { lock (results) return results.Count >= 1; })).Should().BeTrue();
        await Task.Delay(200);
        renders.Should().Be(1);
        lock (results) results.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Newer_Request_Cancels_The_Render_In_Flight_And_Drops_Its_Result()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var results = new List<byte[]?>();
        using var refresher = new LivePreviewRefresher(
            Short,
            async ct =>
            {
                var n = Interlocked.Increment(ref call);
                if (n == 1)
                {
                    first.SetResult();
                    await release.Task; // 取り消されても終わらない描画 (Skia は ct を見ない)
                    return [1];
                }
                return [2];
            },
            b => { lock (results) results.Add(b); });

        refresher.Request();
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refresher.Request(); // 1 本目の描画中に新しい要求
        release.SetResult();

        (await WaitUntilAsync(() => { lock (results) return results.Count >= 1; })).Should().BeTrue();
        await Task.Delay(100);
        lock (results)
        {
            results.Should().ContainSingle("古い描画の結果は捨てる");
            results[0].Should().Equal(2);
        }
    }

    [Fact]
    public async Task Renders_Never_Overlap()
    {
        var running = 0;
        var maxRunning = 0;
        var done = 0;
        using var refresher = new LivePreviewRefresher(
            TimeSpan.Zero,
            async ct =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref maxRunning, now);
                await Task.Delay(40, CancellationToken.None);
                Interlocked.Decrement(ref running);
                return [1];
            },
            _ => Interlocked.Increment(ref done));

        for (var i = 0; i < 5; i++)
        {
            refresher.Request();
            await Task.Delay(10);
        }

        (await WaitUntilAsync(() => Volatile.Read(ref done) >= 1)).Should().BeTrue();
        await Task.Delay(200);
        maxRunning.Should().Be(1, "描画は同時に 1 本だけ");
    }

    [Fact]
    public async Task A_Failing_Render_Is_Reported_As_Null_Not_Thrown()
    {
        var results = new List<byte[]?>();
        using var refresher = new LivePreviewRefresher(
            Short,
            _ => throw new IOException("boom"),
            b => { lock (results) results.Add(b); });

        refresher.Request();

        (await WaitUntilAsync(() => { lock (results) return results.Count >= 1; })).Should().BeTrue();
        lock (results) results[0].Should().BeNull();
    }

    [Fact]
    public async Task Nothing_Is_Reported_After_Dispose_Even_For_A_Render_In_Flight()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = 0;
        var refresher = new LivePreviewRefresher(
            Short,
            async _ =>
            {
                started.SetResult();
                await release.Task;
                return [1];
            },
            _ => Interlocked.Increment(ref reported));

        refresher.Request();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refresher.Dispose();
        release.SetResult();
        refresher.Request(); // 破棄後の要求も無視される
        await Task.Delay(200);

        reported.Should().Be(0);
    }

    [Fact]
    public async Task Disposing_The_Subscription_Releases_The_Viewmodels_Reference_To_The_Callback_Target()
    {
        // R10: 閉じたプレビューのコールバック (= ウィンドウ) を、 ワークスペースの寿命まで保持してはならない。
        await using var h = await AppViewModelHarness.CreateAsync(new AutoConfirmationService());
        WeakReference weak = StartAndDispose(h);

        for (var i = 0; i < 5 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }

        h.Workspace.Output.IsLivePreviewActive.Should().BeFalse("停止後は VM が自動更新を保持しない");
        weak.IsAlive.Should().BeFalse("閉じたウィンドウ相当のオブジェクトが回収できる");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference StartAndDispose(AppViewModelHarness h)
    {
        var fakeWindow = new object();
        var subscription = h.Workspace.Output.StartLivePreview(bytes => GC.KeepAlive(fakeWindow));
        h.Workspace.Output.IsLivePreviewActive.Should().BeTrue();
        var weak = new WeakReference(fakeWindow);
        subscription.Dispose();
        return weak;
    }

    [Fact]
    public async Task ScopedGridRenderer_Uses_A_Different_DbContext_Than_The_Root_Scope()
    {
        // 実際の DI 配線 (AddInfrastructure + AddApplication) で、 描画に使う DbContext が
        // アプリが共有している (ルートの) DbContext と別物であること = 保存・再読込と並行に走れることを確認する。
        var dir = TestImageFactory.CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(dir.FullName, dir.FullName, "test");
        services.AddApplication();
        var used = new List<ViewGridDbContext>();
        services.AddScoped(sp =>
        {
            lock (used) used.Add(sp.GetRequiredService<ViewGridDbContext>());
            return ActivatorUtilities.CreateInstance<RenderGridUseCase>(sp);
        });
        await using var provider = services.BuildServiceProvider();
        await provider.ApplyMigrationsAsync();
        try
        {
            var shared = provider.GetRequiredService<ViewGridDbContext>();
            var grid = new GridCanvas
            {
                Id = Guid.NewGuid(), Name = "g", GridRows = 1, GridCols = 1,
                ColWeights = GridCanvas.UniformWeights(1), RowWeights = GridCanvas.UniformWeights(1),
                CanvasSize = new PixelSize(16, 16),
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            (await provider.GetRequiredService<IGridCanvasRepository>().AddAsync(grid)).IsError.Should().BeFalse();

            var renderer = new ScopedGridRenderer(provider.GetRequiredService<IServiceScopeFactory>());
            var first = await renderer.RenderAsync(grid.Id, new RenderOptions(TrimMode.None));
            var second = await renderer.RenderAsync(grid.Id, new RenderOptions(TrimMode.None));

            first.IsError.Should().BeFalse("専用スコープでも、 保存済みの DB の値を読んで実際に描画できる");
            first.Value.Should().NotBeEmpty();
            second.IsError.Should().BeFalse();
            lock (used)
            {
                used.Should().HaveCount(2);
                used.Should().NotContain(c => ReferenceEquals(c, shared), "共有コンテキストへ触れない");
                used[0].Should().NotBeSameAs(used[1], "描画ごとに新しいコンテキスト");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { dir.Delete(recursive: true); } catch (IOException) { /* 一時領域の後始末に失敗しても結果は変わらない */ }
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }
}
