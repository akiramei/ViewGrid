using System;
using System.Collections.Concurrent;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Imaging;

/// <summary>
/// <see cref="AutoCropSettings"/> ごとの走査結果を <see cref="AutoCropFraction"/>
/// （0–1 比率）でプロセス内にキャッシュする。Renderer は原画像サイズに、View は
/// サムネサイズに比率を掛けることで同一座標系の bbox を共有でき、サムネ走査
/// （圧縮ノイズで精度が落ちる）と原画像走査の不整合を避けられる。<br/>
/// 256 件を超えたら全クリア（簡易 LRU）。画像数は通常数十なので適切な eviction は不要。
/// プロセスを再起動すれば消えるため、メモリ漏れの懸念は限定的。
/// </summary>
public sealed class AutoCropCache
{
    private const int MaxEntries = 256;

    private readonly ConcurrentDictionary<CacheKey, AutoCropFraction> _cache = new();

    /// <summary>
    /// 指定キーの計算結果を取得。無ければ <paramref name="compute"/> を呼んで結果を保存する。
    /// </summary>
    public AutoCropFraction GetOrCompute(Guid assetId, AutoCropSettings settings, Func<AutoCropFraction> compute)
    {
        ArgumentNullException.ThrowIfNull(compute);
        var key = new CacheKey(assetId, settings);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // 競合での重複計算は許容（純粋関数なので結果は同一）
        var computed = compute();

        if (_cache.Count >= MaxEntries)
            _cache.Clear();
        _cache[key] = computed;
        return computed;
    }

    /// <summary>
    /// 指定キーの計算結果がキャッシュにあれば取得。無ければ <c>false</c>。
    /// 副作用なしで参照したいフロー（描画後の geometry 計算等）で使う。
    /// </summary>
    public bool TryGet(Guid assetId, AutoCropSettings settings, out AutoCropFraction fraction)
    {
        var key = new CacheKey(assetId, settings);
        return _cache.TryGetValue(key, out fraction);
    }

    /// <summary>指定 Asset のキャッシュを破棄（Asset 削除時など）。</summary>
    public void InvalidateAsset(Guid assetId)
    {
        foreach (var key in _cache.Keys)
        {
            if (key.AssetId == assetId)
                _cache.TryRemove(key, out _);
        }
    }

    /// <summary>全キャッシュを破棄（テスト用 / 再ロード用）。</summary>
    public void Clear() => _cache.Clear();

    private readonly record struct CacheKey(Guid AssetId, AutoCropSettings Settings);
}
