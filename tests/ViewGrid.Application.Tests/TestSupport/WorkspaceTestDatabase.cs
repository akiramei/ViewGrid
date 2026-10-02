using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ViewGrid.Core.Entities;
using ViewGrid.Infrastructure.Persistence;

namespace ViewGrid.Application.Tests.TestSupport;

/// <summary>
/// ワークスペースのインポート検証用に、 本番と同じスキーマの SQLite ファイルを作る。
/// <b>実際のマイグレーション</b> (<see cref="ViewGridDbContext"/>) で作るので、 実テーブル名 (<c>image_assets</c> など)
/// ・カラム・履歴テーブルが本物と一致する。 以前は手書きのテーブル (<c>ImageAssets</c>) で作っていたため、
/// 検証コードが実スキーマと食い違った名前を見ていても、 テストは通ってしまった。
/// </summary>
internal static class WorkspaceTestDatabase
{
    /// <summary>実マイグレーションで DB を作り、 参照画像の相対パスごとに <see cref="ImageAsset"/> を 1 件ずつ登録する。</summary>
    public static void Create(string dbPath, params string[] referencedImageRelativePaths)
    {
        using var db = Open(dbPath);
        db.Database.Migrate();

        var index = 0;
        foreach (var path in referencedImageRelativePaths)
        {
            index++;
            db.ImageAssets.Add(new ImageAsset
            {
                Id = Guid.NewGuid(),
                SourceType = ImageSource.File,
                OriginalFilename = $"seed-{index}.png",
                StoredRelativePath = path,
                Size = new PixelSize(10, 10),
                FileHash = $"hash{index:D60}",
                FileSizeBytes = 4,
                MimeType = "image/png",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        db.SaveChanges();
        db.Database.CloseConnection();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// 最新より 1 つ前のマイグレーションまでしか適用していない DB (= 旧版で作ったバックアップ) を作る。
    /// 起動時のマイグレーションが追いつかせられるので、 開ける DB として受け入れられなければならない。
    /// </summary>
    public static void CreateWithoutLatestMigration(string dbPath)
    {
        using var db = Open(dbPath);
        var migrations = db.Database.GetMigrations().ToList();
        migrations.Count.Should().BeGreaterThan(1, "旧版相当の DB を作るには 2 つ以上のマイグレーションが要る");
        db.GetService<IMigrator>().Migrate(migrations[^2]);
        db.Database.CloseConnection();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// 既存の DB からテーブルを 1 つ消す。 マイグレーション履歴は「適用済み」 のままなので、
    /// 起動時のマイグレーションは何もせず、 そのテーブルを読んだ時点で初めて落ちる状態を作る (旧版の欠落 DB の再現)。
    /// </summary>
    public static void DropTable(string dbPath, string table)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE \"{table}\";";
        command.ExecuteNonQuery();
    }

    private static ViewGridDbContext Open(string dbPath)
    {
        var options = new DbContextOptionsBuilder<ViewGridDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString())
            .Options;
        return new ViewGridDbContext(options);
    }
}
