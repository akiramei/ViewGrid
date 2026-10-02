namespace ViewGrid.Application.Caching;

/// <summary>
/// 作成コストの高い、 破棄が必要なオブジェクト (焼き込み済みの Bitmap など) の LRU キャッシュ。
/// 上限を超えて追い出された値は、 その場では破棄せず「退避」 に置き、 <see cref="Sweep"/> で
/// 「もう使われていない」 と判定できたものだけを破棄する。
/// </summary>
/// <remarks>
/// <para>
/// 画面の Image は Source の Bitmap を複製せず同じインスタンスを参照して、 描画・計測のたびに使う。
/// 追い出しと同時に破棄すると、 表示中の画像が破棄済みになり、 次の描画・レイアウトで例外 / 空白になる
/// (キャッシュ上限を超える種類の画像を同時に表示すると起きる)。 追い出しの時点では、 値がまだ画面へ接続される前
/// (作った直後) かもしれず、 接続先も 1 か所とは限らない。 そのため追い出しの時点では「表示中か」 を判断せず、
/// 常に退避しておき、 画面の状態が確定した後に <see cref="Sweep"/> で一括して判定する。
/// </para>
/// <para>スレッドセーフではない (UI スレッド専用)。</para>
/// </remarks>
public sealed class DeferredDisposalLruCache<TKey, TValue>
    where TKey : notnull
    where TValue : class, IDisposable
{
    private readonly int _capacity;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _lru = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _index = new();
    private readonly List<TValue> _retired = [];

    public DeferredDisposalLruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>キャッシュに載っている値の数 (退避中のものは含まない)。</summary>
    public int Count => _lru.Count;

    /// <summary>追い出されて、 まだ破棄されずに退避中の値の数。</summary>
    public int RetiredCount => _retired.Count;

    /// <summary>退避中の値があるか (<see cref="Sweep"/> が必要か)。</summary>
    public bool HasRetired => _retired.Count > 0;

    /// <summary>
    /// <paramref name="key"/> の値を返す。 無ければ <paramref name="factory"/> で作ってキャッシュする。
    /// 上限を超えたら最も古い値を追い出して退避する (破棄はしない)。
    /// </summary>
    public TValue GetOrCreate(TKey key, Func<TValue> factory)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddLast(node);
            return node.Value.Value;
        }

        var value = factory();
        _index[key] = _lru.AddLast(new KeyValuePair<TKey, TValue>(key, value));

        while (_lru.Count > _capacity)
        {
            var oldest = _lru.First!;
            _lru.RemoveFirst();
            _index.Remove(oldest.Value.Key);
            _retired.Add(oldest.Value.Value);
        }
        return value;
    }

    /// <summary>
    /// 退避中の値のうち、 <paramref name="isInUse"/> が <c>false</c> (もう画面から切り離された) ものを破棄する。
    /// 使われているものは次回の <see cref="Sweep"/> まで退避に残す。 画面の状態が確定した後 (再構築の完了後など) に呼ぶ。
    /// </summary>
    public void Sweep(Func<TValue, bool> isInUse)
    {
        ArgumentNullException.ThrowIfNull(isInUse);
        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            var value = _retired[i];
            if (isInUse(value)) continue;
            _retired.RemoveAt(i);
            value.Dispose();
        }
    }
}
