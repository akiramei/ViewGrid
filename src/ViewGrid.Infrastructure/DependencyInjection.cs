using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;
using ViewGrid.Infrastructure.Persistence;
using ViewGrid.Infrastructure.Repositories;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string dataDirectory)
    {
        var storageOptions = new StorageOptions { DataDirectory = dataDirectory };
        services.AddSingleton(storageOptions);

        var dbPath = System.IO.Path.Combine(dataDirectory, "viewgrid.db");
        services.AddDbContext<ViewGridDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<IImageAssetRepository, EfImageAssetRepository>();
        services.AddScoped<IImageCopyRepository, EfImageCopyRepository>();
        services.AddScoped<IGridCanvasRepository, EfGridCanvasRepository>();
        services.AddScoped<IGridPlacementRepository, EfGridPlacementRepository>();

        services.AddSingleton<IImageHasher, Sha256ImageHasher>();
        services.AddSingleton<IImageProber, SkiaImageProber>();
        services.AddSingleton<IImageStorage, FileSystemImageStorage>();
        services.AddSingleton<IThumbnailService, SkiaThumbnailService>();
        services.AddSingleton<AutoCropCache>();
        services.AddSingleton<IAutoCropBboxResolver, SkiaAutoCropBboxResolver>();
        services.AddSingleton<IGridImageRenderer, SkiaGridImageRenderer>();

        return services;
    }

    /// <summary>
    /// 起動時に保留中のマイグレーションを適用する。
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ViewGridDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
