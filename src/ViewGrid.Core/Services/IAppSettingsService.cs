using ViewGrid.Core.Settings;

namespace ViewGrid.Core.Services;

/// <summary>
/// <see cref="AppSettings"/> の読み取り / 永続化 / 変更通知を担うサービス。
/// 起動時に singleton として構築され、 設定 JSON を同期的に読み込む。
/// 設定ダイアログ等から <see cref="SaveAsync"/> が呼ばれると新しい値で
/// <see cref="Current"/> を更新 + JSON 書き出し + <see cref="Changed"/> 発火する。
/// </summary>
public interface IAppSettingsService
{
    /// <summary>現在有効な設定値。 起動以後は <see cref="SaveAsync"/> 経由でのみ更新される。</summary>
    AppSettings Current { get; }

    /// <summary>新しい設定で永続化し、 <see cref="Current"/> を差し替えて <see cref="Changed"/> を発火する。</summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>
    /// 設定変更時に発火するイベント。 引数は新しい <see cref="AppSettings"/>。
    /// テーマ即時反映など UI 側の動的更新に購読する。
    /// </summary>
    event EventHandler<AppSettings>? Changed;
}
