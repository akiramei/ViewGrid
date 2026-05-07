using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IFilePickerService _filePicker;
    private bool _suppressSave;

    /// <summary>
    /// エクスポート / インポート結果のステータス文字列 (例: 「インポートしました: ...」)。
    /// 空文字なら表示しない。 成功 / 失敗どちらも HintText 1 行で View 側に出す。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIoStatus))]
    public partial string IoStatus { get; set; } = string.Empty;

    public bool HasIoStatus => !string.IsNullOrEmpty(IoStatus);

    /// <summary>
    /// 直近の SaveAsync チェイン。 OnXChanged は fire-and-forget で settings.json を書き出すため、
    /// テストや確実に永続化を待ちたい呼び出し元はこのタスクを await する。
    /// </summary>
    public Task PendingSaveTask { get; private set; } = Task.CompletedTask;

    /// <summary>テーマ (Default / Light / Dark)。 文字列で持つのは <see cref="AppSettings.Theme"/> と整合させるため。</summary>
    [ObservableProperty] public partial string Theme { get; set; } = "Default";

    /// <summary>アクセント色プリセット ID (例: "Sky")。 <see cref="AccentColorPresets"/> 参照。</summary>
    [ObservableProperty] public partial string AccentColor { get; set; } = "Sky";

    /// <summary>新規論理コピー作成時の既定スケーリング。</summary>
    [ObservableProperty] public partial ScalingMode DefaultScalingMode { get; set; } = ScalingMode.UniformContain;

    /// <summary>AutoCrop ON 時の既定プリセット (Custom は対象外)。</summary>
    [ObservableProperty] public partial AutoCropPreset DefaultAutoCropPreset { get; set; } = AutoCropPreset.White;

    /// <summary>サムネイルの最大エッジサイズ (px)。 256 / 512 / 1024 / 2048 から選ぶ。</summary>
    [ObservableProperty] public partial int ThumbnailMaxEdgePixels { get; set; } = 1024;

    /// <summary>
    /// 自動保存 (Inspector / グリッドプロパティの値編集を 1 秒静止後に自動 commit) の ON/OFF。
    /// 既定 OFF (後方互換)。
    /// </summary>
    [ObservableProperty] public partial bool EnableAutoSave { get; set; }

    public SettingsDialogViewModel(IAppSettingsService settings, IFilePickerService filePicker)
    {
        _settings = settings;
        _filePicker = filePicker;
        ReloadFromCurrent();
    }

    /// <summary>
    /// VM の各プロパティを <see cref="IAppSettingsService.Current"/> から再ロードする。
    /// 初期化時 + インポート完了時に呼ばれる。 <see cref="_suppressSave"/> で初期化中の
    /// <c>OnXChanged</c> による fire-and-forget save を抑止する。
    /// </summary>
    private void ReloadFromCurrent()
    {
        _suppressSave = true;
        try
        {
            Theme = _settings.Current.Theme;
            AccentColor = _settings.Current.AccentColor;
            DefaultScalingMode = _settings.Current.DefaultScalingMode;
            DefaultAutoCropPreset = _settings.Current.DefaultAutoCropPreset;
            ThumbnailMaxEdgePixels = _settings.Current.ThumbnailMaxEdgePixels;
            EnableAutoSave = _settings.Current.EnableAutoSave;
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

    /// <summary>アクセント色プリセット選択肢 (View が ItemsControl で色ドット表示)。</summary>
    public IReadOnlyList<AccentColorPreset> AccentColorPresetOptions { get; } = AccentColorPresets.All;

    partial void OnThemeChanged(string value)
    {
        SaveCurrent(s => s with { Theme = value });
        // RadioButton 用ヘルパも追従して再評価させる
        OnPropertyChanged(nameof(IsThemeDefault));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
    }

    partial void OnAccentColorChanged(string value) => SaveCurrent(s => s with { AccentColor = value });
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

    partial void OnEnableAutoSaveChanged(bool value) => SaveCurrent(s => s with { EnableAutoSave = value });

    /// <summary>
    /// Current に対して mutator を適用した新しい <see cref="AppSettings"/> を保存する。
    /// 初期化中 / View 側双方向バインディングのループは <see cref="_suppressSave"/> で抑止。
    /// 並行更新の race は <see cref="IAppSettingsService.UpdateAsync"/> 側の lock で吸収されるため、
    /// VM 側では fire-and-forget で良い (テストや確実な永続化待ちは <see cref="PendingSaveTask"/>)。
    /// </summary>
    private void SaveCurrent(Func<AppSettings, AppSettings> mutate)
    {
        if (_suppressSave) return;
        PendingSaveTask = _settings.UpdateAsync(mutate);
    }

    /// <summary>
    /// 現在の <see cref="AppSettings"/> を JSON ファイルとして書き出す (別 PC / ユーザー間共有用)。
    /// キャンセル時はサイレント終了。 ファイル書き込み失敗時は <see cref="IoStatus"/> にメッセージ。
    /// </summary>
    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        var path = await _filePicker.PickSaveJsonPathAsync("viewgrid-settings.json", "設定をエクスポート");
        if (string.IsNullOrEmpty(path))
        {
            IoStatus = string.Empty;
            return;
        }
        try
        {
            var json = JsonSerializer.Serialize(_settings.Current, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            IoStatus = $"エクスポートしました: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            IoStatus = $"エクスポートに失敗しました: {ex.Message}";
        }
    }

    /// <summary>
    /// JSON ファイルから <see cref="AppSettings"/> を読み込んで <see cref="IAppSettingsService.SaveAsync"/> 経由で
    /// 全置換する。 完了後 VM のプロパティも再ロードして View に即反映する。 不正 JSON / 解析失敗は
    /// <see cref="IoStatus"/> にメッセージ。
    /// </summary>
    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var path = await _filePicker.PickOpenJsonPathAsync("設定をインポート");
        if (string.IsNullOrEmpty(path))
        {
            IoStatus = string.Empty;
            return;
        }
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var imported = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (imported is null)
            {
                IoStatus = "インポートに失敗しました: JSON が空または不正です。";
                return;
            }
            await _settings.SaveAsync(imported);
            ReloadFromCurrent();
            IoStatus = $"インポートしました: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            IoStatus = $"インポートに失敗しました: {ex.Message}";
        }
    }

    /// <summary>
    /// エクスポート / インポート両方で使う JSON オプション。 enum は文字列表現で書く
    /// (人間が手編集できる + バージョン間互換 = "Fill" 等のラベルが安定)。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
