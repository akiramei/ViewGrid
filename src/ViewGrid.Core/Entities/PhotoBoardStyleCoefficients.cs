namespace ViewGrid.Core.Entities;

/// <summary>
/// PhotoBoard レンダリングを駆動する 9 係数。<see cref="PhotoBoardStyle"/> と強度
/// (intensity) から <see cref="For"/> ファクトリで導出する。レンダラ
/// (<c>SkiaGridImageRenderer</c>) と layout (<c>PhotoBoardLayout.Compute</c>) はこの
/// レコードを直接読むだけで「どの程度散らかすか」を判断できる。
///
/// <para>
/// 旧版は単一 <c>chaos</c> を二段階カーブで複数 ramp に分配していたが、その分配が
/// 暗黙的でユーザー側に「ナチュラルとラフの違い」が伝わらない問題があった。係数を
/// 独立にすることで、スタイル毎に「回転量だけ強くして重なりは控える」のような
/// 個性が表現できる。
/// </para>
/// </summary>
/// <param name="FrameStrength">
/// ポラロイド枠の濃度倍率。<c>0.0</c> でフレームなし → <c>1.0</c> でフル可視。
/// 旧 frameRamp に相当。
/// </param>
/// <param name="ShadowStrength">
/// ドロップシャドウの強度倍率。<c>0.0</c> でシャドウなし → <c>1.0</c> でフル可視。
/// 旧 frameRamp に相当 (旧版では shadow = frameRamp と一体化していた)。
/// </param>
/// <param name="RotationStrength">
/// 回転量の倍率。<c>0.0</c> で回転なし → <c>1.0</c> で最大 ±8°。
/// 回転中心オフセットの倍率も兼ねる。
/// </param>
/// <param name="JitterStrength">
/// 位置ジッター倍率 (per-item ジッター + 行/列バイアスの合計)。<c>0.0</c> で
/// 位置ずれなし → <c>1.0</c> で最大 5% + 10% × 2 = 25% (セル短辺比)。
/// </param>
/// <param name="OverlapProbability">
/// per-item 独立で「重なり nudge」を発火させる確率。<c>0.0</c> で重なりなし →
/// <c>1.0</c> で全配置に nudge 適用 (実用上は 0.5 程度が上限の感覚)。
/// </param>
/// <param name="Expansion">
/// グリッド拡張倍率 (≥ 1.0)。各配置の中心がキャンバス中心から外側に
/// (Expansion - 1) × 100% 広がる。<c>1.0</c> で拡張なし、<c>1.40</c> で 40% 拡張。
/// </param>
/// <param name="DriftStrength">
/// 全体ドリフト方向の強さ。<c>0.0</c> でドリフトなし → <c>1.0</c> で最大 6%
/// (セル短辺比) のシフトを全配置に同方向に加える。
/// </param>
/// <param name="AnchorDecay">
/// アンカー (主役) 配置の offset / rotation 減衰倍率。<c>0.30</c> = 30% の動きしか
/// 許容しない (= 70% 抑制)。<c>1.0</c> でアンカー機構を実質無効化、<c>0.0</c> で
/// 完全固定。Polish pass からも除外される。
/// </param>
/// <param name="PolishEnabled">
/// 4 種ガード (孤立 / 過密 / 整列破壊 / ペア分解) を走らせるか。<c>true</c> で
/// 後処理を実行 (構造的な配置の偶発的破綻を最小限の手数で回避)。
/// </param>
public sealed record PhotoBoardStyleCoefficients(
    double FrameStrength,
    double ShadowStrength,
    double RotationStrength,
    double JitterStrength,
    double OverlapProbability,
    double Expansion,
    double DriftStrength,
    double AnchorDecay,
    bool PolishEnabled)
{
    /// <summary>
    /// スタイル毎の基準係数 (intensity = 0.5 のときの値)。private static で固定。
    /// 将来追加スタイルがあれば enum 値 + ここへの追記だけで済む。
    /// </summary>
    private static readonly Dictionary<PhotoBoardStyle, PhotoBoardStyleCoefficients> StyleBase = new()
    {
        [PhotoBoardStyle.Natural] = new PhotoBoardStyleCoefficients(
            FrameStrength: 1.00,
            ShadowStrength: 1.00,
            RotationStrength: 0.30,
            JitterStrength: 0.30,
            OverlapProbability: 0.00,
            Expansion: 1.10,
            DriftStrength: 0.30,
            AnchorDecay: 0.30,
            PolishEnabled: true),

        [PhotoBoardStyle.Rough] = new PhotoBoardStyleCoefficients(
            FrameStrength: 1.00,
            ShadowStrength: 1.00,
            RotationStrength: 0.60,
            JitterStrength: 0.60,
            OverlapProbability: 0.15,
            Expansion: 1.20,
            DriftStrength: 0.50,
            AnchorDecay: 0.30,
            PolishEnabled: true),

        [PhotoBoardStyle.Scattered] = new PhotoBoardStyleCoefficients(
            FrameStrength: 1.00,
            ShadowStrength: 1.00,
            RotationStrength: 0.90,
            JitterStrength: 0.90,
            OverlapProbability: 0.40,
            Expansion: 1.35,
            DriftStrength: 0.70,
            AnchorDecay: 0.30,
            PolishEnabled: true),
    };

    /// <summary>
    /// 指定スタイルの基準係数を取得。enum の不正値には Natural を返す (ガード)。
    /// </summary>
    public static PhotoBoardStyleCoefficients Base(PhotoBoardStyle style) =>
        StyleBase.TryGetValue(style, out var coefs) ? coefs : StyleBase[PhotoBoardStyle.Natural];

    /// <summary>
    /// スタイル + 強度から実効係数を計算する。
    /// <paramref name="intensity"/> は <c>[0.0, 1.0]</c> で、UI 上のスライダー値。
    /// 0.5 で「スタイル基準値そのまま」、0.0 で「ほぼ整列モード」、1.0 で「最大効果」。
    ///
    /// <para>
    /// スケール式: <c>factor = 2.0 × intensity</c> ([0, 2] の範囲)。
    /// Rotation / Jitter / Overlap / Drift は基準値 × factor を [0, 1] にクランプ。
    /// Expansion は <c>1.0 + (base - 1.0) × factor</c>。
    /// Frame / Shadow / AnchorDecay / Polish は intensity に影響されない (スタイルの
    /// 「写真らしさ」は強度を変えても保持されるべき)。
    /// </para>
    /// </summary>
    public static PhotoBoardStyleCoefficients For(PhotoBoardStyle style, double intensity)
    {
        var baseCoefs = Base(style);
        var clamped = Math.Clamp(intensity, 0.0, 1.0);
        var factor = 2.0 * clamped;

        return new PhotoBoardStyleCoefficients(
            FrameStrength: baseCoefs.FrameStrength,
            ShadowStrength: baseCoefs.ShadowStrength,
            RotationStrength: Math.Clamp(baseCoefs.RotationStrength * factor, 0.0, 1.0),
            JitterStrength: Math.Clamp(baseCoefs.JitterStrength * factor, 0.0, 1.0),
            OverlapProbability: Math.Clamp(baseCoefs.OverlapProbability * factor, 0.0, 1.0),
            Expansion: Math.Max(1.0, 1.0 + (baseCoefs.Expansion - 1.0) * factor),
            DriftStrength: Math.Clamp(baseCoefs.DriftStrength * factor, 0.0, 1.0),
            AnchorDecay: baseCoefs.AnchorDecay,
            PolishEnabled: baseCoefs.PolishEnabled);
    }

    /// <summary>整列状態 (係数すべて 0、フレーム / シャドウのみ可視)。レンダラ単体テストや
    /// スタイル無指定時のフォールバックに使う。</summary>
    public static PhotoBoardStyleCoefficients Aligned { get; } = new(
        FrameStrength: 1.0, ShadowStrength: 1.0,
        RotationStrength: 0.0, JitterStrength: 0.0, OverlapProbability: 0.0,
        Expansion: 1.0, DriftStrength: 0.0, AnchorDecay: 0.30, PolishEnabled: true);

    /// <summary>素のグリッド (フレーム + シャドウすら無い、 TrimMode.None と同等)。</summary>
    public static PhotoBoardStyleCoefficients Off { get; } = new(
        FrameStrength: 0.0, ShadowStrength: 0.0,
        RotationStrength: 0.0, JitterStrength: 0.0, OverlapProbability: 0.0,
        Expansion: 1.0, DriftStrength: 0.0, AnchorDecay: 0.30, PolishEnabled: false);
}
