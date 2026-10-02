using Microsoft.Data.Sqlite;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// ワークスペースのインポート検証用に、最小限の「ViewGrid の DB らしい」 SQLite ファイルを作る。
/// EF のマイグレーション履歴テーブルと、参照画像の相対パスを持つ <c>ImageAssets</c> テーブルだけを持つ。
/// </summary>
internal static class WorkspaceTestDatabase
{
    public static void Create(string dbPath, params string[] referencedImageRelativePaths)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Execute(connection, "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);");
        Execute(connection, "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260101000000_Initial', '10.0.0');");
        Execute(connection, "CREATE TABLE \"ImageAssets\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"StoredRelativePath\" TEXT NOT NULL);");
        foreach (var path in referencedImageRelativePaths)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO \"ImageAssets\" VALUES ($id, $path);";
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$path", path);
            insert.ExecuteNonQuery();
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
