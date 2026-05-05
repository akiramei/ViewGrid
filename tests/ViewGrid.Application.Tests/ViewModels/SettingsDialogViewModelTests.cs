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
            DefaultScalingMode = ScalingMode.UniformCover,
            DefaultAutoCropPreset = AutoCropPreset.Black,
            ThumbnailMaxEdgePixels = 512,
        });

        var vm = new SettingsDialogViewModel(fake);

        vm.Theme.Should().Be("Dark");
        vm.IsThemeDark.Should().BeTrue();
        vm.DefaultScalingMode.Should().Be(ScalingMode.UniformCover);
        vm.DefaultAutoCropPreset.Should().Be(AutoCropPreset.Black);
        vm.ThumbnailMaxEdgePixels.Should().Be(512);
        vm.IsThumb512.Should().BeTrue();
    }

    [Fact]
    public void Initial_Load_Does_Not_Trigger_Save()
    {
        // 初期化時の代入で SaveAsync が呼ばれると不要 I/O が起きるので抑止されていること
        var fake = new FakeSettingsService(new AppSettings());

        _ = new SettingsDialogViewModel(fake);

        fake.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public void Theme_Change_Saves_New_Settings()
    {
        var fake = new FakeSettingsService(new AppSettings { Theme = "Default" });
        var vm = new SettingsDialogViewModel(fake);

        vm.Theme = "Light";

        fake.SaveCallCount.Should().Be(1);
        fake.LastSaved.Should().NotBeNull();
        fake.LastSaved!.Theme.Should().Be("Light");
    }

    [Fact]
    public void ThumbnailSize_Change_Via_Helper_Saves()
    {
        var fake = new FakeSettingsService(new AppSettings { ThumbnailMaxEdgePixels = 1024 });
        var vm = new SettingsDialogViewModel(fake);

        vm.IsThumb2048 = true;

        fake.LastSaved.Should().NotBeNull();
        fake.LastSaved!.ThumbnailMaxEdgePixels.Should().Be(2048);
    }

    [Fact]
    public void DefaultScaling_And_AutoCrop_Changes_Save_Independently()
    {
        var fake = new FakeSettingsService(new AppSettings());
        var vm = new SettingsDialogViewModel(fake);

        vm.DefaultScalingMode = ScalingMode.Fill;
        vm.DefaultAutoCropPreset = AutoCropPreset.Transparent;

        fake.SaveCallCount.Should().Be(2);
        fake.LastSaved!.DefaultScalingMode.Should().Be(ScalingMode.Fill);
        fake.LastSaved.DefaultAutoCropPreset.Should().Be(AutoCropPreset.Transparent);
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
    }
}
