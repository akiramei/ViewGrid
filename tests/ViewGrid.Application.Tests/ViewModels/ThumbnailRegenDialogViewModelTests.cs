using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class ThumbnailRegenDialogViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private RegenerateThumbnailsUseCase _useCase = null!;
    private ThumbnailRegenDialogViewModel _vm = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new RegenerateThumbnailsUseCase(
            _fx.AssetRepository, _fx.Thumbnails,
            NullLogger<RegenerateThumbnailsUseCase>.Instance);
        _vm = new ThumbnailRegenDialogViewModel(_useCase);
    }

    public async Task DisposeAsync()
    {
        _vm.Dispose();
        await _fx.DisposeAsync();
    }

    [Fact]
    public void Initial_State_IsDormant()
    {
        _vm.Total.Should().Be(0);
        _vm.Completed.Should().Be(0);
        _vm.IsRunning.Should().BeFalse();
        _vm.IsCompleted.Should().BeFalse();
        _vm.IsCancelled.Should().BeFalse();
        _vm.ProgressPercent.Should().Be(0);
        _vm.StartCommand.CanExecute(null).Should().BeTrue();
        _vm.CancelCommand.CanExecute(null).Should().BeFalse();
        _vm.CloseCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_With_Empty_Repository_CompletesImmediately()
    {
        // 空 DB → Total=0, Successful=0、 IsCompleted=true で終わる
        await _vm.StartCommand.ExecuteAsync(null);

        _vm.IsRunning.Should().BeFalse();
        _vm.IsCompleted.Should().BeTrue();
        _vm.IsCancelled.Should().BeFalse();
        _vm.Total.Should().Be(0);
        _vm.Successful.Should().Be(0);
        _vm.CompletionMessage.Should().Contain("再生成が完了しました");
    }

    [Fact]
    public async Task StartAsync_With_Assets_UpdatesProgressAndCompletes()
    {
        await _fx.SeedAssetAsync(fileHash: "vmtest0000000000000000000000000000000000000000000000000000000001");
        await _fx.SeedAssetAsync(fileHash: "vmtest0000000000000000000000000000000000000000000000000000000002");

        await _vm.StartCommand.ExecuteAsync(null);

        _vm.IsCompleted.Should().BeTrue();
        _vm.Total.Should().Be(2);
        _vm.Successful.Should().Be(2);
        _vm.Completed.Should().Be(2);
        _vm.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public async Task After_Completion_StartCannotBeReExecuted()
    {
        await _vm.StartCommand.ExecuteAsync(null);
        _vm.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CloseRequested_Fires_When_CloseCommand_Executed()
    {
        var closed = false;
        _vm.CloseRequested += (_, _) => closed = true;

        _vm.CloseCommand.Execute(null);

        closed.Should().BeTrue();
    }
}
