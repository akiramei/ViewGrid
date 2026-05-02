using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.History;
using ViewGrid.Application.Messages;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class CopyPropertiesViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private CopyPropertiesViewModel _vm = null!;
    private WeakReferenceMessenger _messenger = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        var update = new UpdateImageCopyUseCase(_fx.CopyRepository, _fx.PlacementRepository, _fx.GridRepository);
        _messenger = new WeakReferenceMessenger();
        var history = new UndoRedoService();
        _vm = new CopyPropertiesViewModel(
            update, history, _messenger, _fx.ColorPicker, _fx.AutoCropResolver,
            NullLogger<CopyPropertiesViewModel>.Instance);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public void Attach_Null_Resets_State_And_HasCopy_Becomes_False()
    {
        _vm.Attach(null);

        _vm.HasCopy.Should().BeFalse();
        _vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task ScalingMode_Other_Than_Fill_Activates_Alignment()
    {
        // 旧版は ScalingMode.None で TrimmingAnchor、それ以外で Alignment という
        // 排他制御だったが、TrimmingAnchor は Alignment に統合されたため
        // None / Uniform 系 / Cover では常に Alignment が有効。Fill のみ無効。
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.ScalingMode = ScalingMode.None;
        _vm.IsAlignmentActive.Should().BeTrue();

        _vm.ScalingMode = ScalingMode.UniformCover;
        _vm.IsAlignmentActive.Should().BeTrue();

        _vm.ScalingMode = ScalingMode.UniformContain;
        _vm.IsAlignmentActive.Should().BeTrue();
    }

    [Fact]
    public async Task ScalingMode_Fill_Deactivates_Alignment()
    {
        // Fill はセルにピッタリ伸縮されるため Alignment が効かない。
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.ScalingMode = ScalingMode.Fill;

        _vm.IsAlignmentActive.Should().BeFalse();
    }

    [Fact]
    public async Task Attach_Loads_Source_Values_Without_Marking_Dirty()
    {
        var source = await SeedSourceAsync();

        _vm.Attach(source);

        _vm.HasCopy.Should().BeTrue();
        _vm.IsDirty.Should().BeFalse();
        _vm.Rotation.Should().Be(source.Rotation);
        _vm.ScalingMode.Should().Be(source.ScalingMode);
    }

    [Fact]
    public async Task Editing_A_Field_Marks_Dirty()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.Rotation = Rotation.Cw90;

        _vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_Persists_Changes_Clears_Dirty_And_Updates_Source()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);
        _vm.Rotation = Rotation.Cw180;
        _vm.ScalingMode = ScalingMode.UniformCover;

        await _vm.SaveAsync();

        _vm.IsDirty.Should().BeFalse();
        _vm.StatusMessage.Should().Be("保存しました。");

        // 永続化の確認
        var reloaded = await _fx.CopyRepository.FindByIdAsync(source.CopyId);
        reloaded.Should().NotBeNull();
        reloaded!.Transform.Rotation.Should().Be(Rotation.Cw180);
        reloaded.ScalingMode.Should().Be(ScalingMode.UniformCover);

        // source (リスト側 VM) への反映
        source.Rotation.Should().Be(Rotation.Cw180);
    }

    [Fact]
    public async Task Revert_Restores_Edit_Buffer_From_Source()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);
        _vm.Rotation = Rotation.Cw90;
        _vm.IsDirty.Should().BeTrue();

        _vm.Revert();

        _vm.Rotation.Should().Be(source.Rotation);
        _vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Re_Attach_Same_Source_Clears_Unsaved_Changes()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);
        _vm.FlipX = true;
        _vm.IsDirty.Should().BeTrue();

        _vm.Attach(source);

        _vm.IsDirty.Should().BeFalse();
        _vm.FlipX.Should().Be(source.FlipX);
    }

    [Fact]
    public async Task SaveAsync_Sends_CopyLibraryChangedMessage()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);
        _vm.Rotation = Rotation.Cw90;

        var receivedCount = 0;
        var listener = new object();
        _messenger.Register<CopyLibraryChangedMessage>(listener, (_, _) => receivedCount++);

        try
        {
            await _vm.SaveAsync();
            receivedCount.Should().Be(1);
        }
        finally
        {
            _messenger.UnregisterAll(listener);
        }
    }

    private async Task<CopyItemViewModel> SeedSourceAsync()
    {
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "seed");
        return new CopyItemViewModel(copy);
    }

    [Fact]
    public async Task Attach_With_AutoCrop_Disabled_Has_No_Preview()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.AutoCropEnabled.Should().BeFalse();
        _vm.AutoCropPreviewFraction.Should().BeNull();
        _vm.HasAutoCropPreview.Should().BeFalse();
        _vm.AutoCropPreviewMessage.Should().BeNull();
    }

    [Fact]
    public async Task Disabling_AutoCrop_After_Enable_Clears_Preview()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);
        _vm.AutoCropEnabled = true;
        // 走査の完了を最大 500ms 待つ（resolver は実画像走査だが SourceImagePath が null なので即 null になる）
        for (var i = 0; i < 10 && _vm.AutoCropPreviewMessage is null && _vm.AutoCropPreviewFraction is null; i++)
            await Task.Delay(20);

        _vm.AutoCropEnabled = false;
        for (var i = 0; i < 10 && _vm.AutoCropPreviewFraction is not null; i++)
            await Task.Delay(20);

        _vm.AutoCropPreviewFraction.Should().BeNull();
        _vm.AutoCropPreviewMessage.Should().BeNull();
    }

    [Fact]
    public async Task Enabling_AutoCrop_Without_SourcePath_Keeps_Preview_Null()
    {
        var source = await SeedSourceAsync();
        // SeedSourceAsync は SourceImagePath を null で生成する（実画像連携は別の経路でテスト）
        _vm.Attach(source);

        _vm.AutoCropEnabled = true;
        for (var i = 0; i < 10; i++) await Task.Delay(20); // 計算が走る時間を確保

        _vm.AutoCropPreviewFraction.Should().BeNull(); // SourceImagePath null で early return
    }

    [Theory]
    [InlineData("#FFFFFF", 0xFFFFFFFFu)]
    [InlineData("#000000", 0xFF000000u)]
    [InlineData("FF8800", 0xFFFF8800u)] // # 省略
    [InlineData("#80FF8800", 0x80FF8800u)] // ARGB
    [InlineData("invalid", 0xFFFFFFFFu)] // 不正値 → 既定（白）
    [InlineData("", 0xFFFFFFFFu)] // 空 → 既定
    [InlineData(null, 0xFFFFFFFFu)] // null → 既定
    public void ParseHexColorOrDefault_Returns_Expected_Argb(string? hex, uint expected)
    {
        var argb = CopyPropertiesViewModel.ParseHexColorOrDefault(hex);
        argb.Should().Be(expected);
    }

    [Fact]
    public async Task PickColorFromThumbnailAsync_Switches_To_Custom_And_Samples_Original_Image()
    {
        // SeedAssetAsync が生成する PNG は cornflower blue (#6495ED) でフィルされる
        // (TestImageFactory.CreatePng の既定色)。色採取は SourceImagePath（原画像）から行う。
        // サムネ座標 → 原画像座標の換算もテストする（仮想サムネ寸法 40x40 → 原画像 100x100）。
        var asset = await _fx.SeedAssetAsync();
        var copy = await _fx.SeedCopyAsync(asset.Id);
        var sourcePath = _fx.Storage.ResolveAbsolutePath(asset.StoredRelativePath);
        var source = new CopyItemViewModel(copy,
            thumbnailPath: sourcePath,  // テスト用にサムネ = 原画像で代用
            sourceImagePath: sourcePath,
            sourceWidth: asset.Size.Width,
            sourceHeight: asset.Size.Height);

        _vm.Attach(source);
        _vm.AutoCropEnabled = true;

        // サムネ寸法 40x40 と仮定した中央クリック → 原画像 (50, 50) に換算される
        await _vm.PickColorFromThumbnailAsync(20, 20, 40, 40);

        _vm.AutoCropPreset.Should().Be(AutoCropPreset.Custom);
        _vm.AutoCropCustomColorHex.Should().Be("#6495ED");
        _vm.IsAutoCropCustom.Should().BeTrue();
        _vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Custom_AutoCrop_Round_Trip_Through_Save_And_Attach()
    {
        // Custom + 任意 HEX で Save → 再 Attach → Custom 復元 + HEX 復元
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.AutoCropEnabled = true;
        _vm.AutoCropPreset = AutoCropPreset.Custom;
        _vm.AutoCropCustomColorHex = "#123456";
        _vm.AutoCropThreshold = 16;
        await _vm.SaveAsync();

        // 別 Attach で初期化 → 元の source に再 Attach
        _vm.Attach(null);
        _vm.Attach(source);

        _vm.AutoCropEnabled.Should().BeTrue();
        _vm.AutoCropPreset.Should().Be(AutoCropPreset.Custom);
        _vm.AutoCropCustomColorHex.Should().Be("#123456");
        _vm.AutoCropThreshold.Should().Be(16);
    }

    [Fact]
    public async Task ManualCropEnabled_Turning_On_Disables_AutoCropEnabled()
    {
        // 排他連動: 手動を ON にすると自動が OFF になる
        var source = await SeedSourceWithSizeAsync();
        _vm.Attach(source);
        _vm.AutoCropEnabled = true;

        _vm.ManualCropEnabled = true;

        _vm.AutoCropEnabled.Should().BeFalse();
        _vm.ManualCropEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task AutoCropEnabled_Turning_On_Disables_ManualCropEnabled()
    {
        // 排他連動: 自動を ON にすると手動が OFF になる
        var source = await SeedSourceWithSizeAsync();
        _vm.Attach(source);
        _vm.ManualCropEnabled = true;
        _vm.ManualCropPixelX = 10;
        _vm.ManualCropPixelY = 10;
        _vm.ManualCropPixelWidth = 50;
        _vm.ManualCropPixelHeight = 60;

        _vm.AutoCropEnabled = true;

        _vm.ManualCropEnabled.Should().BeFalse();
        _vm.AutoCropEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ManualCrop_With_Defined_Rect_Round_Trips_Through_Save_And_Attach()
    {
        // 100x100 画像で (10,20) から 30x40 を Manual で切る → 比率 (0.1, 0.2, 0.3, 0.4)
        var source = await SeedSourceWithSizeAsync(width: 100, height: 100);
        _vm.Attach(source);

        _vm.ManualCropEnabled = true;
        _vm.ManualCropPixelX = 10;
        _vm.ManualCropPixelY = 20;
        _vm.ManualCropPixelWidth = 30;
        _vm.ManualCropPixelHeight = 40;
        await _vm.SaveAsync();

        _vm.Attach(null);
        _vm.Attach(source);

        _vm.ManualCropEnabled.Should().BeTrue();
        _vm.ManualCropPixelX.Should().BeApproximately(10, 0.001);
        _vm.ManualCropPixelY.Should().BeApproximately(20, 0.001);
        _vm.ManualCropPixelWidth.Should().BeApproximately(30, 0.001);
        _vm.ManualCropPixelHeight.Should().BeApproximately(40, 0.001);
        _vm.IsManualCropDefined.Should().BeTrue();
    }

    [Fact]
    public async Task ManualCropEnabled_Without_Rect_Persists_As_Off()
    {
        // 「手動」ラジオを選んだ直後（矩形未確定 W=0）で Save → 実質 OFF として保存される
        var source = await SeedSourceWithSizeAsync();
        _vm.Attach(source);

        _vm.ManualCropEnabled = true;
        // 矩形は未確定（W=H=0 のまま）
        _vm.IsManualCropDefined.Should().BeFalse();
        await _vm.SaveAsync();

        // 再 Attach すると ManualCropEnabled は false（永続化されたのは null）
        _vm.Attach(null);
        _vm.Attach(source);

        _vm.ManualCropEnabled.Should().BeFalse();
    }

    private async Task<CopyItemViewModel> SeedSourceWithSizeAsync(int width = 100, int height = 100)
    {
        var asset = await _fx.SeedAssetAsync(width: width, height: height);
        var copy = await _fx.SeedCopyAsync(asset.Id, copyName: "seed");
        return new CopyItemViewModel(copy, sourceWidth: width, sourceHeight: height);
    }
}
