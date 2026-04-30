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
        var update = new UpdateImageCopyUseCase(_fx.CopyRepository);
        _messenger = new WeakReferenceMessenger();
        var history = new UndoRedoService();
        _vm = new CopyPropertiesViewModel(
            update, history, _messenger, _fx.ColorPicker,
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
        _vm.OccupyWidth.Should().Be(source.OccupySize.Width);
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
        _vm.OccupyWidth = 3;
        _vm.ScalingMode = ScalingMode.UniformCover;

        await _vm.SaveAsync();

        _vm.IsDirty.Should().BeFalse();
        _vm.StatusMessage.Should().Be("保存しました。");

        // 永続化の確認
        var reloaded = await _fx.CopyRepository.FindByIdAsync(source.CopyId);
        reloaded.Should().NotBeNull();
        reloaded!.Transform.Rotation.Should().Be(Rotation.Cw180);
        reloaded.OccupySize.Width.Should().Be(3);
        reloaded.ScalingMode.Should().Be(ScalingMode.UniformCover);

        // source (リスト側 VM) への反映
        source.Rotation.Should().Be(Rotation.Cw180);
        source.OccupySize.Width.Should().Be(3);
    }

    [Fact]
    public async Task Revert_Restores_Edit_Buffer_From_Source()
    {
        var source = await SeedSourceAsync();
        _vm.Attach(source);
        _vm.Rotation = Rotation.Cw90;
        _vm.OccupyHeight = 5;
        _vm.IsDirty.Should().BeTrue();

        _vm.Revert();

        _vm.Rotation.Should().Be(source.Rotation);
        _vm.OccupyHeight.Should().Be(source.OccupySize.Height);
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
}
