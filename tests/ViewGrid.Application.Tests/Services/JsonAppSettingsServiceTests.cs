using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Settings;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

public sealed class JsonAppSettingsServiceTests : IAsyncLifetime
{
    private DirectoryInfo _tempDir = null!;
    private StorageOptions _options = null!;

    public Task InitializeAsync()
    {
        _tempDir = TestImageFactory.CreateTempDirectory();
        _options = new StorageOptions { DataDirectory = _tempDir.FullName };
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_tempDir.Exists)
            _tempDir.Delete(recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public void File_Missing_Falls_Back_To_Defaults()
    {
        // settings.json が無い → 既定値で起動
        var service = new JsonAppSettingsService(_options, NullLogger<JsonAppSettingsService>.Instance);

        service.Current.Theme.Should().Be("Default");
        service.Current.AccentColor.Should().Be("Sky");
        service.Current.DefaultScalingMode.Should().Be(ScalingMode.UniformContain);
        service.Current.DefaultAutoCropPreset.Should().Be(AutoCropPreset.White);
        service.Current.ThumbnailMaxEdgePixels.Should().Be(1024);
    }

    [Fact]
    public async Task Save_And_Reload_Round_Trips_All_Fields()
    {
        // 全 5 フィールドが Save → 別インスタンスで Load して同じ値で復元
        var service = new JsonAppSettingsService(_options, NullLogger<JsonAppSettingsService>.Instance);
        var newSettings = new AppSettings
        {
            Theme = "Dark",
            AccentColor = "Emerald",
            DefaultScalingMode = ScalingMode.UniformCover,
            DefaultAutoCropPreset = AutoCropPreset.Transparent,
            ThumbnailMaxEdgePixels = 2048,
        };

        await service.SaveAsync(newSettings);

        var reloaded = new JsonAppSettingsService(_options, NullLogger<JsonAppSettingsService>.Instance);
        reloaded.Current.Theme.Should().Be("Dark");
        reloaded.Current.AccentColor.Should().Be("Emerald");
        reloaded.Current.DefaultScalingMode.Should().Be(ScalingMode.UniformCover);
        reloaded.Current.DefaultAutoCropPreset.Should().Be(AutoCropPreset.Transparent);
        reloaded.Current.ThumbnailMaxEdgePixels.Should().Be(2048);
    }

    [Fact]
    public async Task Save_Updates_Current_And_Fires_Changed_Event()
    {
        var service = new JsonAppSettingsService(_options, NullLogger<JsonAppSettingsService>.Instance);
        AppSettings? eventArgs = null;
        service.Changed += (_, s) => eventArgs = s;

        var newSettings = new AppSettings { Theme = "Light" };
        await service.SaveAsync(newSettings);

        service.Current.Theme.Should().Be("Light");
        eventArgs.Should().NotBeNull();
        eventArgs!.Theme.Should().Be("Light");
    }

    [Fact]
    public void Corrupted_Json_Falls_Back_To_Defaults()
    {
        // 壊れた JSON が置かれていたら既定値で復帰 (起動失敗ではなく続行)
        var settingsPath = Path.Combine(_tempDir.FullName, "settings.json");
        File.WriteAllText(settingsPath, "{ this is not valid json");

        var service = new JsonAppSettingsService(_options, NullLogger<JsonAppSettingsService>.Instance);

        service.Current.Theme.Should().Be("Default");
        service.Current.ThumbnailMaxEdgePixels.Should().Be(1024);
    }
}
