using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.Localization;
using ViewGrid.Core.Services;
using ViewGrid.Presentation.Localization;
using ViewGrid.Presentation.Services;

namespace ViewGrid.Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // AvaloniaFilePickerService は MainWindow 参照を保持するため Singleton。
        services.AddSingleton<AvaloniaFilePickerService>();
        services.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<AvaloniaFilePickerService>());

        // LocService は XAML の {loc:Tr} MarkupExtension からも Instance で参照される
        // Singleton。 VM 側からは ILocalizationService 経由で同じインスタンスを引く。
        services.AddSingleton<ILocalizationService>(_ => LocService.Instance);
        return services;
    }
}
