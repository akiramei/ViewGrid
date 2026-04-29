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
            update, history, _messenger,
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
    public async Task ScalingMode_None_Activates_TrimAnchor_And_Deactivates_Alignment()
    {
        // ScalingMode.None ではトリミング基準が renderer に効き、Alignment は効かない。
        // UI 側で IsEnabled に Bind してグレーアウト切替する。
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.ScalingMode = ScalingMode.None;

        _vm.IsTrimAnchorActive.Should().BeTrue();
        _vm.IsAlignmentActive.Should().BeFalse();
    }

    [Fact]
    public async Task ScalingMode_NonNone_Activates_Alignment_And_Deactivates_TrimAnchor()
    {
        // ScalingMode.None 以外（Uniform 系・Cover・Fill）では Alignment が renderer に効く。
        var source = await SeedSourceAsync();
        _vm.Attach(source);

        _vm.ScalingMode = ScalingMode.UniformCover;

        _vm.IsTrimAnchorActive.Should().BeFalse();
        _vm.IsAlignmentActive.Should().BeTrue();
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
}
