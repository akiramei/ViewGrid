using ErrorOr;
using ViewGrid.Core.Settings;

namespace ViewGrid.Core.Services;

/// <summary>
/// ワークスペースの一覧 / 作成 / リネーム / 削除 / アクティブ切替を司る。
/// 切替は <c>active.json</c> 書き換えのみで完結し、 実際にアプリ動作を切り替えるには再起動が必要。
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// ワークスペース一覧を取得する。 <c>workspaces.json</c> が存在しない場合は、
    /// 現在 active なワークスペース 1 件のみのリストを返す (移行直後の自動補完)。
    /// </summary>
    Task<IReadOnlyList<WorkspaceManifest>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// 新規ワークスペースを作成する。 ディレクトリ <c>workspaces/&lt;name&gt;</c> を作成し、
    /// <c>workspaces.json</c> に追記する。 同名の既存ワークスペース、 命名規約違反は拒否される。
    /// </summary>
    Task<ErrorOr<WorkspaceManifest>> CreateAsync(string name, string displayName, CancellationToken ct = default);

    /// <summary>
    /// 既存ワークスペースの DB / 画像 / サムネを丸ごとコピーして新ワークスペースを作る。
    /// 「現状をベースに派生を作る」 「実験用にバックアップを取る」 用途。 source は active でも可
    /// (read のみ)。 SQLite は単一ユーザー前提のため WAL ファイルが残っていてもコピーで実用上問題ない。
    /// </summary>
    Task<ErrorOr<WorkspaceManifest>> DuplicateAsync(
        string sourceName, string newName, string newDisplayName, CancellationToken ct = default);

    /// <summary>
    /// 既存ワークスペースの表示名を変更する (内部識別名 <see cref="WorkspaceManifest.Name"/> は不変)。
    /// </summary>
    Task<ErrorOr<Success>> RenameAsync(string name, string newDisplayName, CancellationToken ct = default);

    /// <summary>
    /// ワークスペースを削除する。 即削除ではなく <c>workspaces/.trash/&lt;name&gt;-&lt;timestamp&gt;</c> へ
    /// 移動して、 ユーザーが手動で復元・完全削除できる余地を残す。
    /// 現在 active なワークスペースは削除できない (アプリが操作する DB を消すため)。
    /// </summary>
    Task<ErrorOr<Success>> DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// 次回起動時に開くワークスペースを設定する。 <c>active.json</c> を書き換えるだけで、
    /// 現在のプロセス内では切り替わらない。 切替反映にはアプリ再起動が必要。
    /// </summary>
    Task<ErrorOr<Success>> SetActiveAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// ワークスペース全体 (DB / assets / thumbnails) を 1 つの zip にまとめて
    /// <paramref name="destinationZipPath"/> に書き出す。 zip ルートに <c>workspace.json</c>
    /// (Name / DisplayName / ExportedAt) を含めるため、 インポート側でデフォルト名候補に使える。
    /// アクティブワークスペースのエクスポートも許可される (SQLite WAL/SHM が残っていても整合性は保たれる前提)。
    /// </summary>
    Task<ErrorOr<Success>> ExportAsync(
        string sourceName, string destinationZipPath, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ExportAsync"/> で生成した zip を読み込み、 <paramref name="newName"/> /
    /// <paramref name="newDisplayName"/> で新ワークスペースとして展開する。 <c>workspaces/&lt;newName&gt;/</c>
    /// を作成 + zip 内のファイル群を配置 + manifest に追記。 既存名と衝突する場合は拒否される。
    /// 不正な zip / <c>workspace.json</c> 不在の場合は <see cref="ErrorOr.ErrorType.Validation"/> を返す。
    /// </summary>
    Task<ErrorOr<WorkspaceManifest>> ImportAsync(
        string sourceZipPath, string newName, string newDisplayName, CancellationToken ct = default);

    /// <summary>
    /// zip ルートの <c>workspace.json</c> から内部名 / 表示名を best-effort で読み出す。
    /// インポートカードのデフォルト候補表示用。 不正な zip / メタデータ無しは <c>null</c>。
    /// </summary>
    Task<WorkspaceExportInfo?> PeekExportInfoAsync(string sourceZipPath, CancellationToken ct = default);
}
