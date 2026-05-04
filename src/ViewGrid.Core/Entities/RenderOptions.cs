namespace ViewGrid.Core.Entities;

/// <summary>
/// プレビュー / PNG 出力時のレンダリングオプション集約。<see cref="OutputMode"/> と
/// <see cref="TrimMode"/>、PhotoBoard 固有の係数 / シードを 1 レコードにまとめる。
/// レンダラ I/F のシグネチャ汚染を防ぎ、新パラメータ追加時もこのレコードを足すだけで貫流可能。
/// 永続化はせず、実行時のオプションとして用いる。
/// </summary>
/// <param name="TrimMode">切り出し方法。<see cref="Entities.TrimMode"/> 参照。</param>
/// <param name="OutputMode">出力モード。通常 / 写真ボード。<see cref="Entities.OutputMode"/> 参照。</param>
/// <param name="PhotoBoardCoefficients">
/// PhotoBoard レンダリングを駆動する 9 係数。<see cref="Entities.OutputMode.PhotoBoard"/> 時は
/// 必須 (null は ArgumentException)。<see cref="Entities.OutputMode.Normal"/> 時は無視されるので
/// null で構わない。
/// </param>
/// <param name="PhotoBoardSeedOverride">
/// PhotoBoard モードの決定論的 PRNG シード上書き。通常は <c>null</c> で <c>GridCanvas.Id</c>
/// 由来のシードを使うが、テスト等で固定値を注入したい場合に指定。
/// </param>
public sealed record RenderOptions(
    TrimMode TrimMode = TrimMode.None,
    OutputMode OutputMode = OutputMode.Normal,
    PhotoBoardStyleCoefficients? PhotoBoardCoefficients = null,
    ulong? PhotoBoardSeedOverride = null)
{
    /// <summary>既定オプション (通常モード + 全面出力、PhotoBoard 無効)。</summary>
    public static RenderOptions Default { get; } = new();
}
