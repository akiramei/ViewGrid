using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.History;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using Xunit;

namespace ViewGrid.Application.Tests.ViewModels;

public sealed class GridCanvasListViewModelTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private GridCanvasListViewModel _vm = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        var create = new CreateGridCanvasUseCase(_fx.GridRepository);
        var delete = new DeleteGridCanvasUseCase(_fx.GridRepository);
        var rename = new RenameGridCanvasUseCase(_fx.GridRepository);
        var setActive = new SetActiveGridCanvasUseCase(_fx.GridRepository);
        var history = new UndoRedoService();
        _vm = new GridCanvasListViewModel(
            _fx.GridRepository, create, delete, rename, setActive, history,
            NullLogger<GridCanvasListViewModel>.Instance);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task LoadAsync_On_Empty_Repository_Results_In_Empty_List()
    {
        await _vm.LoadAsync();

        _vm.Grids.Should().BeEmpty();
        _vm.SelectedGrid.Should().BeNull();
    }

    [Fact]
    public async Task BeginCreate_Sets_Drafts_And_IsCreating()
    {
        _vm.BeginCreate();

        _vm.IsCreating.Should().BeTrue();
        _vm.DraftRows.Should().Be(3);
        _vm.DraftCols.Should().Be(3);
        _vm.DraftCanvasWidth.Should().Be(1200);
        _vm.DraftCanvasHeight.Should().Be(1200);
    }

    [Fact]
    public async Task ConfirmCreateAsync_Adds_Grid_Closes_Form_And_Selects_New_One()
    {
        _vm.BeginCreate();
        _vm.DraftName = "new-grid";
        _vm.DraftRows = 4;
        _vm.DraftCols = 5;

        await _vm.ConfirmCreateAsync();

        _vm.IsCreating.Should().BeFalse();
        _vm.Grids.Should().HaveCount(1);
        _vm.SelectedGrid.Should().NotBeNull();
        _vm.SelectedGrid!.Name.Should().Be("new-grid");
        _vm.SelectedGrid.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmCreateAsync_With_Invalid_Input_Surfaces_Status_Message()
    {
        _vm.BeginCreate();
        _vm.DraftRows = 0; // 不正

        await _vm.ConfirmCreateAsync();

        _vm.Grids.Should().BeEmpty();
        _vm.StatusMessage.Should().NotBeNullOrEmpty();
        _vm.IsCreating.Should().BeTrue(); // フォームは閉じない
    }

    [Fact]
    public async Task ActivateSelectedAsync_Updates_IsActive_Exclusively()
    {
        _vm.BeginCreate();
        _vm.DraftName = "a";
        await _vm.ConfirmCreateAsync();

        _vm.BeginCreate();
        _vm.DraftName = "b";
        await _vm.ConfirmCreateAsync(); // b がアクティブ

        _vm.SelectedGrid = _vm.Grids.First(g => g.Name == "a");
        await _vm.ActivateSelectedAsync();

        _vm.Grids.Single(g => g.Name == "a").IsActive.Should().BeTrue();
        _vm.Grids.Single(g => g.Name == "b").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSelectedAsync_Removes_Grid_And_Reselects_First_Remaining()
    {
        _vm.BeginCreate();
        _vm.DraftName = "a";
        await _vm.ConfirmCreateAsync();

        _vm.BeginCreate();
        _vm.DraftName = "b";
        await _vm.ConfirmCreateAsync();

        var toDelete = _vm.SelectedGrid!;
        await _vm.DeleteSelectedAsync();

        _vm.Grids.Should().NotContain(toDelete);
        _vm.Grids.Should().HaveCount(1);
        _vm.SelectedGrid.Should().NotBeNull();
    }
}
