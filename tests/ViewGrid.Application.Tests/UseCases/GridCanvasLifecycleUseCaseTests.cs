using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;

namespace ViewGrid.Application.Tests.UseCases;

/// <summary>
/// Delete / Rename / SetActive の薄いユースケースをまとめて検証する。
/// </summary>
public sealed class GridCanvasLifecycleUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private CreateGridCanvasUseCase _create = null!;
    private DeleteGridCanvasUseCase _delete = null!;
    private RenameGridCanvasUseCase _rename = null!;
    private SetActiveGridCanvasUseCase _setActive = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _create = new CreateGridCanvasUseCase(_fx.GridRepository);
        _delete = new DeleteGridCanvasUseCase(_fx.GridRepository);
        _rename = new RenameGridCanvasUseCase(_fx.GridRepository);
        _setActive = new SetActiveGridCanvasUseCase(_fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Delete_Removes_Grid_From_Repository()
    {
        var created = await _create.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "temp", Rows = 2, Cols = 2, CanvasWidth = 400, CanvasHeight = 400,
        });

        var result = await _delete.ExecuteAsync(created.Value.Id);

        result.IsError.Should().BeFalse();
        var reloaded = await _fx.GridRepository.FindByIdAsync(created.Value.Id);
        reloaded.Should().BeNull();
    }

    [Fact]
    public async Task Rename_Updates_Name_And_Timestamp()
    {
        var created = await _create.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "before", Rows = 2, Cols = 2, CanvasWidth = 400, CanvasHeight = 400,
        });

        await Task.Delay(50);
        var result = await _rename.ExecuteAsync(created.Value.Id, "after");

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("after");
        result.Value.UpdatedAt.Should().BeAfter(created.Value.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_Rejects_Blank_Name(string name)
    {
        var created = await _create.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "keep", Rows = 2, Cols = 2, CanvasWidth = 400, CanvasHeight = 400,
        });

        var result = await _rename.ExecuteAsync(created.Value.Id, name);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Rename_For_Missing_Grid_Returns_NotFound()
    {
        var result = await _rename.ExecuteAsync(Guid.NewGuid(), "never");
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task SetActive_Switches_Active_Grid_Exclusively()
    {
        var first = await _create.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "a", Rows = 2, Cols = 2, CanvasWidth = 400, CanvasHeight = 400,
        });
        var second = await _create.ExecuteAsync(new CreateGridCanvasRequest
        {
            Name = "b", Rows = 2, Cols = 2, CanvasWidth = 400, CanvasHeight = 400, SetAsActive = false,
        });

        var result = await _setActive.ExecuteAsync(second.Value.Id);

        result.IsError.Should().BeFalse();
        var all = await _fx.GridRepository.FindAllAsync();
        all.Count(g => g.IsActive).Should().Be(1);
        all.Single(g => g.IsActive).Id.Should().Be(second.Value.Id);

        // 最初のものは非アクティブ化されている
        var firstReloaded = await _fx.GridRepository.FindByIdAsync(first.Value.Id);
        firstReloaded!.IsActive.Should().BeFalse();
    }
}
