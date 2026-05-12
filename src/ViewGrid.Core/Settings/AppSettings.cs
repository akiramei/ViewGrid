using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Settings;

/// <summary>
/// アプリのユーザー設定。 ハードコードしていた UI / 既定値を集約し、
/// 設定ダイアログから変更 + JSON 永続化する。 record で immutable に扱い、
/// 変更時は新しい <see cref="AppSettings"/> を作って <see cref="Services.IAppSettingsService.SaveAsync"/> へ渡す。
/// </summary>
public sealed record AppSettings
{
    /// <summary>テーマ。 "Default" (システム追従) / "Light" / "Dark"。</summary>
    public string Theme { get; init; } = "Default";

    /// <summary>アクセント色プリセット ID (例: "Sky"、 "Emerald")。 <see cref="AccentColorPresets"/> 参照。</summary>
    public string AccentColor { get; init; } = "Sky";

    /// <summary>新規論理コピー作成時の既定スケーリング。</summary>
    public ScalingMode DefaultScalingMode { get; init; } = ScalingMode.UniformContain;

    /// <summary>AutoCrop を ON にしたときの既定プリセット (Custom は対象外)。</summary>
    public AutoCropPreset DefaultAutoCropPreset { get; init; } = AutoCropPreset.White;

    /// <summary>サムネイルの最大エッジサイズ (px)。 256 / 512 / 1024 / 2048 を想定。</summary>
    public int ThumbnailMaxEdgePixels { get; init; } = 1024;

    /// <summary>
    /// 最後に開いていたグリッドの Id。 起動時の「直前のセッションの続き」 復元に使う。
    /// Guid? を JSON 上で扱いやすくするため string で保持
    /// (空 / 不正値は null 扱いで先頭グリッドにフォールバック)。
    /// </summary>
    public string? LastOpenedGridId { get; init; }

    /// <summary>
    /// 自動保存の有効/無効。 ON のとき、 ユーザーが Inspector やグリッドプロパティで値を編集すると、
    /// 1 秒間操作が静止した時点で「保存」 ボタンを押さなくても自動的に永続化される。
    /// 既定 false (後方互換)。
    /// </summary>
    public bool EnableAutoSave { get; init; }

    /// <summary>
    /// UI 表示言語の選択。 値は以下のいずれか:
    /// <list type="bullet">
    ///   <item><c>"system"</c>: システムロケールに従う (既定)</item>
    ///   <item><c>"ja"</c>: 日本語</item>
    ///   <item><c>"en"</c>: 英語</item>
    /// </list>
    /// 切替は <see cref="ViewGrid.Core.Services.IAppSettingsService.UpdateAsync"/> 経由で行い、
    /// LocService が PropertyChanged 経由で全 UI を即時更新する (アプリ再起動不要)。
    /// </summary>
    public string Language { get; init; } = "system";
}
