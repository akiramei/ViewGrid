using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Persistence;

public sealed class ViewGridDbContext(DbContextOptions<ViewGridDbContext> options) : DbContext(options)
{
    public DbSet<ImageAsset> ImageAssets => Set<ImageAsset>();
    public DbSet<ImageCopy> ImageCopies => Set<ImageCopy>();
    public DbSet<GridCanvas> GridCanvases => Set<GridCanvas>();
    public DbSet<GridPlacement> GridPlacements => Set<GridPlacement>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // SQLite は DateTimeOffset の ORDER BY を直接サポートしないため、ISO 8601
        // ラウンドトリップ形式の文字列に変換する（辞書順 = 時系列順となる）。
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToStringConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ViewGridDbContext).Assembly);
    }
}
