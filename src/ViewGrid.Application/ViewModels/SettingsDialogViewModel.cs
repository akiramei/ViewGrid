using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 設定ダイアログの ViewModel。 即時適用 + 自動保存型 (VS Code / Figma 流):
/// プロパティ変更 → 同期的に <see cref="IAppSettingsService.SaveAsync"/> を呼んで JSON へ書き出し、
/// <see cref="IAppSettingsService.Changed"/> 経由で View 側 (テーマ等) が即座に反映する。
/// 「OK / キャンセル」 は無く、 ダイアログ閉じても変更は確定済み。
/// </summary>
public sealed partial class SettingsDialogViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private bool _suppressSave;

    /// <summary>テーマ (Default / Light / Dark)。 文字列で持つのは <see cref="AppSettings.Theme"/> と整合させるため。</summary>
    [ObservableProperty] public partial string Theme { get; set; } = "Default";

    /// <summary>新規論理コピー作成時の既定スケーリング。</summary>
    [ObservableProperty] public partial ScalingMode DefaultScalingMode { get; set; } = ScalingMode.UniformContain;

    /// <summary>AutoCrop ON 時の既定プリセット (Custom は対象外)。</summary>
    [ObservableProperty] public partial AutoCropPreset DefaultAutoCropPreset { get; set; } = AutoCropPreset.White;

    /// <summary>サムネイルの最大エッジサイズ (px)。 256 / 512 / 1024 / 2048 から選ぶ。</summary>
    [ObservableProperty] public partial int ThumbnailMaxEdgePixels { get; set; } = 1024;

    public SettingsDialogViewModel(IAppSettingsService settings)
    {
        _settings = settings;

        // 初期表示は現在の Current から (Save 抑止フラグで初期化中の OnXChanged を黙らせる)
        _suppressSave = true;
        try
        {
            Theme = _settings.Current.Theme;
            DefaultScalingMode = _settings.Current.DefaultScalingMode;
            DefaultAutoCropPreset = _settings.Current.DefaultAutoCropPreset;
            ThumbnailMaxEdgePixels = _settings.Current.ThumbnailMaxEdgePixels;
        }
        finally
        {
            _suppressSave = false;
        }
    }

    /// <summary>Theme RadioButton 用: View 側 IsChecked バインディングを単純化するためのヘルパ。</summary>
    public bool IsThemeDefault
    {
        get => Theme == "Default";
        set { if (value) Theme = "Default"; }
    }

    public bool IsThemeLight
    {
        get => Theme == "Light";
        set { if (value) Theme = "Light"; }
    }

    public bool IsThemeDark
    {
        get => Theme == "Dark";
        set { if (value) Theme = "Dark"; }
    }

    /// <summary>サムネサイズ RadioButton 用 (256 / 512 / 1024 / 2048)。</summary>
    public bool IsThumb256 { get => ThumbnailMaxEdgePixels == 256; set { if (value) ThumbnailMaxEdgePixels = 256; } }
    public bool IsThumb512 { get => ThumbnailMaxEdgePixels == 512; set { if (value) ThumbnailMaxEdgePixels = 512; } }
    public bool IsThumb1024 { get => ThumbnailMaxEdgePixels == 1024; set { if (value) ThumbnailMaxEdgePixels = 1024; } }
    public bool IsThumb2048 { get => ThumbnailMaxEdgePixels == 2048; set { if (value) ThumbnailMaxEdgePixels = 2048; } }

    /// <summary>ScalingMode ComboBox の選択肢 (View が ItemsSource にバインド)。 全 6 値。</summary>
    public IReadOnlyList<ScalingMode> ScalingModeOptions { get; } =
        Enum.GetValues<ScalingMode>();

    /// <summary>AutoCropPreset ComboBox の選択肢 (Custom は除く)。</summary>
    public IReadOnlyList<AutoCropPreset> AutoCropPresetOptions { get; } =
        [AutoCropPreset.White, AutoCropPreset.Black, AutoCropPreset.Transparent];

    partial void OnThemeChanged(string value)
    {
        SaveCurrent(s => s with { Theme = value });
        // RadioButton 用ヘルパも追従して再評価させる
        OnPropertyChanged(nameof(IsThemeDefault));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
    }

    partial void OnDefaultScalingModeChanged(ScalingMode value) => SaveCurrent(s => s with { DefaultScalingMode = value });
    partial void OnDefaultAutoCropPresetChanged(AutoCropPreset value) => SaveCurrent(s => s with { DefaultAutoCropPreset = value });

    partial void OnThumbnailMaxEdgePixelsChanged(int value)
    {
        SaveCurrent(s => s with { ThumbnailMaxEdgePixels = value });
        OnPropertyChanged(nameof(IsThumb256));
        OnPropertyChanged(nameof(IsThumb512));
        OnPropertyChanged(nameof(IsThumb1024));
        OnPropertyChanged(nameof(IsThumb2048));
    }

    /// <summary>
    /// Current に対して mutator を適用した新しい <see cref="AppSettings"/> を保存する。
    /// 初期化中 / View 側双方向バインディングのループは <see cref="_suppressSave"/> で抑止。
    /// </summary>
    private void SaveCurrent(Func<AppSettings, AppSettings> mutate)
    {
        if (_suppressSave) return;
        var newSettings = mutate(_settings.Current);
        // fire and forget: SaveAsync 内でファイル書き出し + Changed 発火
        _ = _settings.SaveAsync(newSettings);
    }
}
