using FluentAssertions;
using ViewGrid.Application.Caching;

namespace ViewGrid.Application.Tests.Caching;

public sealed class DeferredDisposalLruCacheTests
{
    private sealed class Tracked : IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool IsDisposed => DisposeCount > 0;
        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public void Eviction_Alone_Never_Disposes_A_Value_Even_When_Nothing_Is_Known_About_Its_Use()
    {
        var cache = new DeferredDisposalLruCache<int, Tracked>(capacity: 2);
        var values = Enumerable.Range(0, 5).Select(i => cache.GetOrCreate(i, () => new Tracked())).ToList();

        values.Should().OnlyContain(v => !v.IsDisposed, "追い出しの時点では、 表示中かどうかを判断せず破棄しない");
        cache.Count.Should().Be(2);
        cache.RetiredCount.Should().Be(3);
    }

    [Fact]
    public void Sixty_Five_Distinct_Values_Used_At_Once_Are_All_Kept_Until_They_Are_Released()
    {
        // R08: 上限 (64) を超える種類の画像 (保護領域つきの多数の配置など) を同時に表示しても、 表示中の画像を破棄しない。
        var cache = new DeferredDisposalLruCache<int, Tracked>(capacity: 64);
        var onScreen = Enumerable.Range(0, 70).Select(i => cache.GetOrCreate(i, () => new Tracked())).ToList();

        cache.Sweep(v => onScreen.Contains(v)); // 70 件すべてが画面に接続されている

        onScreen.Should().OnlyContain(v => !v.IsDisposed, "接続中の全ての画像が読める");
        cache.RetiredCount.Should().Be(6);
    }

    [Fact]
    public void A_Value_Is_Disposed_Once_After_It_Leaves_The_Screen_And_Never_Again()
    {
        var cache = new DeferredDisposalLruCache<int, Tracked>(capacity: 1);
        var first = cache.GetOrCreate(1, () => new Tracked());
        cache.GetOrCreate(2, () => new Tracked()); // 1 が追い出される

        cache.Sweep(_ => true);
        first.IsDisposed.Should().BeFalse("まだ表示中");

        cache.Sweep(_ => false); // 画面から外れた
        cache.Sweep(_ => false);

        first.DisposeCount.Should().Be(1, "破棄は 1 回だけ");
        cache.RetiredCount.Should().Be(0);
    }

    [Fact]
    public void A_Cached_Value_Is_Not_Disposed_By_Sweep_Even_If_It_Is_Not_On_Screen()
    {
        var cache = new DeferredDisposalLruCache<int, Tracked>(capacity: 3);
        var cached = cache.GetOrCreate(1, () => new Tracked());

        cache.Sweep(_ => false);

        cached.IsDisposed.Should().BeFalse("キャッシュに載っている間は再利用のため保持する");
        cache.GetOrCreate(1, () => throw new InvalidOperationException("再作成してはならない")).Should().BeSameAs(cached);
    }

    [Fact]
    public void Least_Recently_Used_Value_Is_The_One_Evicted()
    {
        var cache = new DeferredDisposalLruCache<int, Tracked>(capacity: 2);
        var a = cache.GetOrCreate(1, () => new Tracked());
        var b = cache.GetOrCreate(2, () => new Tracked());
        cache.GetOrCreate(1, () => new Tracked()); // 1 を使い直す → 2 が最古
        cache.GetOrCreate(3, () => new Tracked());

        cache.Sweep(_ => false);

        b.IsDisposed.Should().BeTrue();
        a.IsDisposed.Should().BeFalse();
    }
}
