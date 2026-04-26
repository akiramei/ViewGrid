using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ViewGrid.Infrastructure.Persistence;

/// <summary>
/// dotnet-ef ツール（マイグレーション生成等）が使うデザインタイムファクトリ。
/// 実行時には <see cref="DependencyInjection.AddInfrastructure"/> 経由で登録される。
/// </summary>
internal sealed class ViewGridDbContextFactory : IDesignTimeDbContextFactory<ViewGridDbContext>
{
    public ViewGridDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ViewGridDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new ViewGridDbContext(options);
    }
}
