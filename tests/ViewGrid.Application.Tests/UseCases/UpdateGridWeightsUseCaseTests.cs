using System.Collections.Immutable;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class UpdateGridWeightsUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private UpdateGridWeightsUseCase _useCase = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _useCase = new UpdateGridWeightsUseCase(_fx.GridRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Updates_ColWeights_While_Preserving_Other_Fields()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);

        var result = await _useCase.ExecuteAsync(grid.Id, [3, 1, 1], rowWeights: null);

        result.IsError.Should().BeFalse();
        result.Value.ColWeights.SequenceEqual([3, 1, 1]).Should().BeTrue();
        result.Value.RowWeights.SequenceEqual(grid.RowWeights).Should().BeTrue();

        var reloaded = await _fx.GridRepository.FindByIdAsync(grid.Id);
        reloaded!.ColWeights.SequenceEqual([3, 1, 1]).Should().BeTrue();
    }

    [Fact]
    public async Task Returns_Validation_Error_When_Count_Mismatches()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);
        var result = await _useCase.ExecuteAsync(grid.Id, [1, 2], rowWeights: null);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_Validation_Error_For_Non_Positive_Weights()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);
        var result = await _useCase.ExecuteAsync(grid.Id, [1, 0, 2], rowWeights: null);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Grid()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), [1, 1], rowWeights: null);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    /// <summary>
    /// 回帰: 列ロックが設定された grid に重み更新を流したとき、 ColLocked / RowLocked が
    /// DB 上で保持されること。 旧実装は new GridCanvas { ... } で ColLocked / RowLocked を
    /// 指定し忘れて default `[]` が書き戻されてしまい、 「列をロックしてから重み調整すると
    /// ロックが消える」 バグになっていた。
    /// </summary>
    [Fact]
    public async Task Preserves_ColLocked_And_RowLocked_When_Updating_Weights()
    {
        var grid = await SeedGridAsync(rows: 2, cols: 3);
        // 中央列だけロック / 上行だけロックの状態を作る
        var lockedCol = ImmutableArray.Create(false, true, false);
        var lockedRow = ImmutableArray.Create(true, false);
        var locks = new UpdateGridLocksUseCase(_fx.GridRepository);
        await locks.ExecuteAsync(grid.Id, lockedCol, lockedRow);

        var result = await _useCase.ExecuteAsync(grid.Id, [3, 1, 1], rowWeights: null);

        result.IsError.Should().BeFalse();
        var reloaded = await _fx.GridRepository.FindByIdAsync(grid.Id);
        reloaded!.ColLocked.SequenceEqual(lockedCol).Should().BeTrue("列ロックが保持されるべき");
        reloaded.RowLocked.SequenceEqual(lockedRow).Should().BeTrue("行ロックが保持されるべき");
    }

    private async Task<GridCanvas> SeedGridAsync(int rows, int cols)
    {
        var grid = new GridCanvas
        {
            Id = Guid.NewGuid(),
            Name = "test",
            GridRows = rows,
            GridCols = cols,
            ColWeights = GridCanvas.UniformWeights(cols),
            RowWeights = GridCanvas.UniformWeights(rows),
            CanvasSize = new PixelSize(600, 600),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var added = await _fx.GridRepository.AddAsync(grid);
        added.IsError.Should().BeFalse();
        return grid;
    }
}
