using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using ViewGrid.Application.History;
using ViewGrid.Application.Services;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Services;

namespace ViewGrid.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // VM 間の疎結合通知に使うメッセンジャー（候補リストの自動更新ほか）
        services.AddSingleton<IMessenger>(_ => WeakReferenceMessenger.Default);

        // Undo/Redo 履歴サービス（アプリ全体で 1 本のスタックを共有）
        services.AddSingleton<IUndoRedoService, UndoRedoService>();

        // ImageCrop 優先順位 Resolver（ManualCrop > AutoCrop > null）
        services.AddSingleton<IImageCropResolver, ImageCropResolver>();

        // UseCases
        services.AddScoped<ImportImageUseCase>();
        services.AddScoped<CreateLogicalCopyUseCase>();
        services.AddScoped<UpdateImageCopyUseCase>();
        services.AddScoped<DeleteImageAssetUseCase>();
        services.AddScoped<CreateGridCanvasUseCase>();
        services.AddScoped<DeleteGridCanvasUseCase>();
        services.AddScoped<RenameGridCanvasUseCase>();
        services.AddScoped<SetActiveGridCanvasUseCase>();
        services.AddScoped<PlaceImageCopyUseCase>();
        services.AddScoped<RemovePlacementUseCase>();
        services.AddScoped<MovePlacementUseCase>();
        services.AddScoped<SwapPlacementsUseCase>();
        services.AddScoped<RenderGridUseCase>();
        services.AddScoped<ExportGridUseCase>();
        services.AddScoped<UpdatePlacementOffsetUseCase>();
        services.AddScoped<UpdatePlacementOccupySizeUseCase>();
        services.AddScoped<UpdateGridWeightsUseCase>();
        services.AddScoped<UpdateGridLocksUseCase>();
        services.AddScoped<FitGridWeightToPlacementUseCase>();
        services.AddScoped<ForkPlacementVariantUseCase>();
        services.AddScoped<RegenerateThumbnailsUseCase>();

        // ViewModels
        services.AddTransient<AssetLibraryViewModel>();
        services.AddTransient<CopyPropertiesViewModel>();
        services.AddTransient<GridCanvasListViewModel>();
        services.AddTransient<PlacementInspectorViewModel>();
        services.AddTransient<GridWorkspaceViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsDialogViewModel>();
        services.AddTransient<ThumbnailRegenDialogViewModel>();

        return services;
    }
}
