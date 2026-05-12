using FluentAssertions;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class SettingsDialogViewModelTests
{
    [Fact]
    public void Initial_Values_Reflect_AppSettings_Current()
    {
        var fake = new FakeSettingsService(new AppSettings
        {
            Theme = "Dark",
            AccentColor = "Emerald",
            DefaultScalingMode = ScalingMode.UniformCover,
            DefaultAutoCropPreset = AutoCropPreset.Black,
            ThumbnailMaxEdgePixels = 512,
        });

        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.Theme.Should().Be("Dark");
        vm.IsThemeDark.Should().BeTrue();
        vm.AccentColor.Should().Be("Emerald");
        vm.DefaultScalingMode.Should().Be(ScalingMode.UniformCover);
        vm.DefaultAutoCropPreset.Should().Be(AutoCropPreset.Black);
        vm.ThumbnailMaxEdgePixels.Should().Be(512);
        vm.IsThumb512.Should().BeTrue();
    }

    [Fact]
    public void AccentColor_Change_Saves_New_Settings()
    {
        var fake = new FakeSettingsService(new AppSettings { AccentColor = "Sky" });
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.AccentColor = "Rose";

        fake.SaveCallCount.Should().Be(1);
        fake.LastSaved!.AccentColor.Should().Be("Rose");
    }

    [Fact]
    public void Initial_Load_Does_Not_Trigger_Save()
    {
        // 初期化時の代入で SaveAsync が呼ばれると不要 I/O が起きるので抑止されていること
        var fake = new FakeSettingsService(new AppSettings());

        _ = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        fake.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public void Theme_Change_Saves_New_Settings()
    {
        var fake = new FakeSettingsService(new AppSettings { Theme = "Default" });
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.Theme = "Light";

        fake.SaveCallCount.Should().Be(1);
        fake.LastSaved.Should().NotBeNull();
        fake.LastSaved!.Theme.Should().Be("Light");
    }

    [Fact]
    public void ThumbnailSize_Change_Via_Helper_Saves()
    {
        var fake = new FakeSettingsService(new AppSettings { ThumbnailMaxEdgePixels = 1024 });
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.IsThumb2048 = true;

        fake.LastSaved.Should().NotBeNull();
        fake.LastSaved!.ThumbnailMaxEdgePixels.Should().Be(2048);
    }

    [Fact]
    public void DefaultScaling_And_AutoCrop_Changes_Save_Independently()
    {
        var fake = new FakeSettingsService(new AppSettings());
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.DefaultScalingMode = ScalingMode.Fill;
        vm.DefaultAutoCropPreset = AutoCropPreset.Transparent;

        fake.SaveCallCount.Should().Be(2);
        fake.LastSaved!.DefaultScalingMode.Should().Be(ScalingMode.Fill);
        fake.LastSaved.DefaultAutoCropPreset.Should().Be(AutoCropPreset.Transparent);
    }

    [Fact]
    public void Language_Change_Saves_New_Settings()
    {
        var fake = new FakeSettingsService(new AppSettings { Language = "system" });
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.Language = "en";

        fake.LastSaved.Should().NotBeNull();
        fake.LastSaved!.Language.Should().Be("en");
        vm.IsLanguageEn.Should().BeTrue();
        vm.IsLanguageJa.Should().BeFalse();
        vm.IsLanguageSystem.Should().BeFalse();
    }

    [Fact]
    public void Language_Helper_Setter_Updates_Language()
    {
        var fake = new FakeSettingsService(new AppSettings { Language = "system" });
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService());

        vm.IsLanguageJa = true;

        vm.Language.Should().Be("ja");
        fake.LastSaved!.Language.Should().Be("ja");
    }

    [Fact]
    public async Task ExportSettings_Cancel_Sets_Empty_IoStatus()
    {
        var fake = new FakeSettingsService(new AppSettings());
        var vm = new SettingsDialogViewModel(fake, new FakeFilePickerService()); // 既定で null (cancel)

        await vm.ExportSettingsCommand.ExecuteAsync(null);

        vm.IoStatus.Should().BeEmpty();
        vm.HasIoStatus.Should().BeFalse();
    }

    [Fact]
    public async Task ImportSettings_From_File_Updates_VM_And_Settings()
    {
        var fake = new FakeSettingsService(new AppSettings { Theme = "Default", AccentColor = "Sky" });
        var tempPath = Path.Combine(Path.GetTempPath(), $"viewgrid-settings-test-{Guid.NewGuid()}.json");
        var importJson = """
            {
              "Theme": "Dark",
              "AccentColor": "Rose",
              "DefaultScalingMode": "Fill",
              "DefaultAutoCropPreset": "Black",
              "ThumbnailMaxEdgePixels": 2048,
              "LastOpenedGridId": null
            }
            """;
        await File.WriteAllTextAsync(tempPath, importJson);
        try
        {
            var picker = new FakeFilePickerService { OpenJsonPath = tempPath };
            var vm = new SettingsDialogViewModel(fake, picker);

            await vm.ImportSettingsCommand.ExecuteAsync(null);

            // SaveAsync で全置換 → VM 側プロパティも ReloadFromCurrent で更新
            vm.Theme.Should().Be("Dark");
            vm.AccentColor.Should().Be("Rose");
            vm.DefaultScalingMode.Should().Be(ScalingMode.Fill);
            vm.DefaultAutoCropPreset.Should().Be(AutoCropPreset.Black);
            vm.ThumbnailMaxEdgePixels.Should().Be(2048);
            vm.IoStatus.Should().Contain("Settings_Import_SuccessFmt");
            fake.LastSaved!.AccentColor.Should().Be("Rose");
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    [Fact]
    public async Task ImportSettings_With_Invalid_Json_Reports_Error_Status()
    {
        var fake = new FakeSettingsService(new AppSettings { AccentColor = "Sky" });
        var tempPath = Path.Combine(Path.GetTempPath(), $"viewgrid-settings-bad-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempPath, "{ invalid json");
        try
        {
            var picker = new FakeFilePickerService { OpenJsonPath = tempPath };
            var vm = new SettingsDialogViewModel(fake, picker);

            await vm.ImportSettingsCommand.ExecuteAsync(null);

            vm.IoStatus.Should().StartWith("Settings_Import_FailFmt");
            vm.AccentColor.Should().Be("Sky"); // 失敗時は現状維持
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    [Fact]
    public async Task ExportSettings_Writes_Json_File()
    {
        var fake = new FakeSettingsService(new AppSettings
        {
            Theme = "Dark",
            AccentColor = "Violet",
        });
        var tempPath = Path.Combine(Path.GetTempPath(), $"viewgrid-settings-export-{Guid.NewGuid()}.json");
        try
        {
            var picker = new FakeFilePickerService { SaveJsonPath = tempPath };
            var vm = new SettingsDialogViewModel(fake, picker);

            await vm.ExportSettingsCommand.ExecuteAsync(null);

            File.Exists(tempPath).Should().BeTrue();
            var written = await File.ReadAllTextAsync(tempPath);
            written.Should().Contain("\"Theme\": \"Dark\"");
            written.Should().Contain("\"AccentColor\": \"Violet\"");
            vm.IoStatus.Should().Contain("Settings_Export_SuccessFmt");
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    /// <summary>
    /// IAppSettingsService の最小限スタブ。 Save 呼び出し回数 / 最後に渡された値を記録する。
    /// </summary>
    private sealed class FakeSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; }
        public int SaveCallCount { get; private set; }
        public AppSettings? LastSaved { get; private set; }

        public event EventHandler<AppSettings>? Changed;

        public FakeSettingsService(AppSettings initial)
        {
            Current = initial;
        }

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            SaveCallCount++;
            LastSaved = settings;
            Current = settings;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default)
        {
            // 同期スタブ: 実装側 (JsonAppSettingsService) は SemaphoreSlim で serialize するが、
            // テストは単一スレッドかつ await 直後に Current 反映を期待するので素直に実行する。
            var newSettings = mutate(Current);
            return SaveAsync(newSettings, ct);
        }
    }

    /// <summary>
    /// IFilePickerService の最小限スタブ。 Save/Open の JSON path はテストから事前にセットして
    /// 「ユーザーがファイルを選んだ」 を再現する。 設定しなければ null (キャンセル相当)。
    /// </summary>
    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? SaveJsonPath { get; set; }
        public string? OpenJsonPath { get; set; }

        public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSavePngPathAsync(string suggestedFileName, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickSaveJsonPathAsync(string suggestedFileName, string title, CancellationToken ct = default) =>
            Task.FromResult(SaveJsonPath);
        public Task<string?> PickOpenJsonPathAsync(string title, CancellationToken ct = default) =>
            Task.FromResult(OpenJsonPath);
    }
}
