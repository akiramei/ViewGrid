using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ViewGrid.Application.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = "ViewGrid";

    /// <summary>現在のタブインデックス（0: 準備、1: 配置）。</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    public AssetLibraryViewModel AssetLibrary { get; }
    public CopyListViewModel CopyList { get; }
    public CopyPropertiesViewModel CopyProperties { get; }
    public GridCanvasListViewModel GridList { get; }
    public GridWorkspaceViewModel GridWorkspace { get; }

    public MainWindowViewModel(
        AssetLibraryViewModel assetLibrary,
        CopyListViewModel copyList,
        CopyPropertiesViewModel copyProperties,
        GridCanvasListViewModel gridList,
        GridWorkspaceViewModel gridWorkspace)
    {
        AssetLibrary = assetLibrary;
        CopyList = copyList;
        CopyProperties = copyProperties;
        GridList = gridList;
        GridWorkspace = gridWorkspace;

        AssetLibrary.PropertyChanged += OnAssetLibraryPropertyChanged;
        CopyList.PropertyChanged += OnCopyListPropertyChanged;
        GridList.PropertyChanged += OnGridListPropertyChanged;
    }

    private async void OnAssetLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssetLibraryViewModel.SelectedAsset))
            return;

        await CopyList.LoadForAssetAsync(AssetLibrary.SelectedAsset?.AssetId);
    }

    private void OnCopyListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CopyListViewModel.SelectedCopy))
            return;

        CopyProperties.Attach(CopyList.SelectedCopy);
    }

    private async void OnGridListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GridCanvasListViewModel.SelectedGrid))
            return;

        await GridWorkspace.LoadGridAsync(GridList.SelectedGrid);
    }
}
