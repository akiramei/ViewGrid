namespace ViewGrid.Core.Entities;

/// <summary>
/// PNG 出力時のレンダリングオプション集約。<see cref="TrimMode"/> と PhotoBoard 固有
/// パラメータを 1 レコードにまとめ、レンダラ I/F のシグネチャ汚染を防ぐ。今後
/// 新しいレンダリング機能を追加する際もこのレコードに項目を足すだけで貫流できる。
/// 永続化はせず、実行時のオプションとして用いる。
/// </summary>
/// <param name="TrimMode">出力モード。<see cref="Entities.TrimMode"/> 参照。</param>
/// <param name="PhotoBoardChaos">
/// PhotoBoard モード時の「整列 ↔ 散らかし」軸の値。<c>0.0</c> でフレーム / シャドウ /
/// 位置ジッター / 回転すべて無効 → <see cref="Entities.TrimMode.None"/> と同一出力。
/// <c>1.0</c> で最大効果。<see cref="Entities.TrimMode.PhotoBoard"/> 以外のモードでは無視。
/// </param>
/// <param name="PhotoBoardSeedOverride">
/// PhotoBoard モード時の決定論的 PRNG シード上書き。通常は <c>null</c> で
/// <c>GridCanvas.Id</c> 由来のシードを使うが、テスト等で固定値を注入したい場合に指定。
/// </param>
public sealed record RenderOptions(
    TrimMode TrimMode = TrimMode.None,
    double PhotoBoardChaos = 0.0,
    ulong? PhotoBoardSeedOverride = null)
{
    /// <summary>既定オプション (<see cref="TrimMode.None"/>、PhotoBoard 無効)。</summary>
    public static RenderOptions Default { get; } = new();
}
