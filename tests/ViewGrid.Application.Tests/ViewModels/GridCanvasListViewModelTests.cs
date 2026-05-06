using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.History;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;

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
        var history = new UndoRedoService();
        _vm = new GridCanvasListViewModel(
            _fx.GridRepository, create, delete, rename, _fx.AppSettings, history,
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
        // 旧仕様: IsActive=true (auto-activate on create)。 「デフォルトグリッド」 概念廃止により
        // 常に IsActive=false で永続化。 「直前に作成 → 自動選択」 の UX は SelectedGrid 直接代入で維持。
        _vm.SelectedGrid.IsActive.Should().BeFalse();
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
    public async Task LoadAsync_Restores_LastOpenedGrid_From_Settings()
    {
        _vm.BeginCreate();
        _vm.DraftName = "a";
        await _vm.ConfirmCreateAsync();

        _vm.BeginCreate();
        _vm.DraftName = "b";
        await _vm.ConfirmCreateAsync();

        // b が最後に作成されたため、 ConfirmCreate 末尾の SelectedGrid = 新規 で b が選ばれており、
        // OnSelectedGridChanged が settings の LastOpenedGridId を b に更新している。
        // ここで a に切替えて settings を a に書き戻す。
        var gridA = _vm.Grids.First(g => g.Name == "a");
        _vm.SelectedGrid = gridA;
        // OnSelectedGridChanged は SaveAsync を fire-and-forget で起動するので、
        // settings.json への書き出しが完了するまで待ってから次のアサーションに進む。
        await _vm.LastOpenedSaveTask;

        // 新しい VM (新セッション相当) を立てて LoadAsync で復元動作を確認する。
        var create = new CreateGridCanvasUseCase(_fx.GridRepository);
        var delete = new DeleteGridCanvasUseCase(_fx.GridRepository);
        var rename = new RenameGridCanvasUseCase(_fx.GridRepository);
        var history = new UndoRedoService();
        var vm2 = new GridCanvasListViewModel(
            _fx.GridRepository, create, delete, rename, _fx.AppSettings, history,
            NullLogger<GridCanvasListViewModel>.Instance);

        await vm2.LoadAsync();

        vm2.SelectedGrid.Should().NotBeNull();
        vm2.SelectedGrid!.Name.Should().Be("a");
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

    // ─── RenameSelectedAsync ガード仕様 ───
    // 中央ヘッダのインライン編集は廃止し、 右ペイン GridPropertiesView から
    // LostFocus / Enter で直接 RenameSelectedAsync を呼ぶ設計に変更。 入力 trim と
    // 「空欄 / 同名は no-op」 のガードは VM 側に集約してテストする。

    [Fact]
    public async Task RenameSelectedAsync_Trims_And_Persists_New_Name()
    {
        _vm.BeginCreate();
        _vm.DraftName = "old";
        await _vm.ConfirmCreateAsync();

        await _vm.RenameSelectedAsync("  new  ");

        _vm.SelectedGrid!.Name.Should().Be("new");
    }

    [Fact]
    public async Task RenameSelectedAsync_With_Whitespace_Skips_Save()
    {
        _vm.BeginCreate();
        _vm.DraftName = "keep";
        await _vm.ConfirmCreateAsync();

        await _vm.RenameSelectedAsync("   ");

        _vm.SelectedGrid!.Name.Should().Be("keep");
    }

    [Fact]
    public async Task RenameSelectedAsync_With_Same_Name_Skips_Save()
    {
        _vm.BeginCreate();
        _vm.DraftName = "same";
        await _vm.ConfirmCreateAsync();

        await _vm.RenameSelectedAsync("same");

        _vm.SelectedGrid!.Name.Should().Be("same");
    }
}
