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
    /// <summary>現在有効な設定値。 起動以後は <see cref="SaveAsync"/> / <see cref="UpdateAsync"/> 経由でのみ更新される。</summary>
    AppSettings Current { get; }

    /// <summary>
    /// 新しい設定で永続化し、 <see cref="Current"/> を差し替えて <see cref="Changed"/> を発火する。
    /// 全置換の用途 (設定 import 等) で使う。 部分更新で複数 producer が並行する場合は
    /// race の元になるので <see cref="UpdateAsync"/> を推奨。
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>
    /// <see cref="Current"/> に <paramref name="mutate"/> を適用した新しい設定を atomic に永続化する。
    /// 内部 lock で全 producer (異なる VM 含む) を serialize するため、 mutate 適用時には
    /// 他の保存が反映済みの最新 <see cref="Current"/> を見る。 「user が Theme を変えてから
    /// grid を切替」 のような cross-VM 並行操作で発生する lost update を防ぐ。
    /// </summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default);

    /// <summary>
    /// 設定変更時に発火するイベント。 引数は新しい <see cref="AppSettings"/>。
    /// テーマ即時反映など UI 側の動的更新に購読する。
    /// 内部 lock の外で発火するため、 ハンドラ内から <see cref="SaveAsync"/> /
    /// <see cref="UpdateAsync"/> を呼んでも deadlock しない。
    /// </summary>
    event EventHandler<AppSettings>? Changed;
}
