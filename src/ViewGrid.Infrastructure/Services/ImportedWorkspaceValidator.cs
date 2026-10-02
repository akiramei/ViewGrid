using ErrorOr;
using Microsoft.Data.Sqlite;

namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// インポートで展開したワークスペースが「開ける状態」かを、登録・切替の前に検証する。
/// zip のメタデータ (workspace.json) とパスの安全性だけを見ていると、壊れた DB や DB 欠落の zip が
/// 「取り込み成功」として登録され、次回起動で SQLite Error 26 になったり、空のワークスペースが
/// 復元成功に見えたりする。ここで拒否すれば、現在の正常な作業環境は変わらない。
/// </summary>
internal static class ImportedWorkspaceValidator
{
    /// <summary>ワークスペース直下の DB ファイル名 (<c>DependencyInjection</c> の接続先と同じ)。</summary>
    internal const string DatabaseFileName = "viewgrid.db";

    /// <summary>EF Core がマイグレーション適用済みの DB に必ず作る履歴テーブル。ViewGrid の DB かの判定に使う。</summary>
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// 展開済みの <paramref name="workspaceDir"/> を検証する。問題なければ <c>null</c>、あれば理由つきの
    /// <see cref="Error"/> (呼び出し側が展開先を掃除して返す)。DB は読み取り専用・プール無しで開く
    /// (検証がファイルをロックしたまま残ると、続く掃除 (削除) が失敗するため)。
    /// </summary>
    internal static Error? Validate(string workspaceDir)
    {
        var dbPath = Path.Combine(workspaceDir, DatabaseFileName);
        if (!File.Exists(dbPath))
            return Error.Validation("Workspace.ImportDatabaseMissing",
                $"zip にデータベース ({DatabaseFileName}) が含まれていません。ViewGrid からエクスポートした zip を指定してください。");

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            if (!PassesQuickCheck(connection))
                return InvalidDatabase();

            if (!TableExists(connection, MigrationsHistoryTable))
                return Error.Validation("Workspace.ImportNotViewGridDatabase",
                    "zip 内のデータベースが ViewGrid のものではありません。ViewGrid からエクスポートした zip を指定してください。");

            return CheckReferencedImages(connection, workspaceDir);
        }
        catch (SqliteException)
        {
            // 「file is not a database」(Error 26) など。Open は遅延評価なので最初のクエリで発生する。
            return InvalidDatabase();
        }
    }

    private static Error InvalidDatabase() =>
        Error.Validation("Workspace.ImportDatabaseCorrupt",
            "zip 内のデータベースが破損しているため取り込めません。別のバックアップ zip を指定してください。");

    private static bool PassesQuickCheck(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        using var reader = command.ExecuteReader();
        // 正常なら 1 行 "ok"。それ以外は問題の内容が行として返る。
        return reader.Read() && string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>
    /// DB が参照する元画像ファイルが zip に含まれているかを確認する。元画像が無いと、取り込み後に
    /// 配置・出力が画像を読めず、編集を再開できない。サムネイルは再生成できるので対象外。
    /// </summary>
    private static Error? CheckReferencedImages(SqliteConnection connection, string workspaceDir)
    {
        if (!TableExists(connection, "ImageAssets"))
            return null;

        var root = Path.GetFullPath(workspaceDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var missing = 0;
        string? firstMissing = null;

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT StoredRelativePath FROM ImageAssets;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var relative = reader.GetString(0);
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                continue;
            missing++;
            firstMissing ??= relative;
        }

        return missing == 0
            ? null
            : Error.Validation("Workspace.ImportAssetsMissing",
                $"データベースが参照する画像ファイルが {missing} 件、zip に含まれていません (例: {firstMissing})。完全なバックアップ zip を指定してください。");
    }
}
