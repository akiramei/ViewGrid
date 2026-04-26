using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Core.Services;
using ViewGrid.Presentation.Services;

namespace ViewGrid.Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // AvaloniaFilePickerService は MainWindow 参照を保持するため Singleton。
        services.AddSingleton<AvaloniaFilePickerService>();
        services.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<AvaloniaFilePickerService>());
        return services;
    }
}
