using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using Xunit;

namespace ViewGrid.Application.Tests.UseCases;

public sealed class FitGridWeightToPlacementUseCaseTests : IAsyncLifetime
{
    private UseCaseFixture _fx = null!;
    private FitGridWeightToPlacementUseCase _useCase = null!;
    private PlaceImageCopyUseCase _place = null!;
    private UpdateGridWeightsUseCase _updateWeights = null!;

    public async Task InitializeAsync()
    {
        _fx = await UseCaseFixture.CreateAsync();
        _updateWeights = new UpdateGridWeightsUseCase(_fx.GridRepository);
        _useCase = new FitGridWeightToPlacementUseCase(
            _fx.GridRepository,
            _fx.PlacementRepository,
            _fx.CopyRepository,
            _fx.AssetRepository,
            _updateWeights,
            NullLogger<FitGridWeightToPlacementUseCase>.Instance);
        _place = new PlaceImageCopyUseCase(_fx.GridRepository, _fx.CopyRepository, _fx.PlacementRepository);
    }

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    /// <summary>
    /// 3x3 均等グリッドの中央セル (1,1) に縦長画像 (100x200) を UniformContain で配置。
    /// セル 200x200 に対し画像描画矩形は 100x200（左右 50 余白）。
    /// 列フィット → 列重みが [1,1,1] から、中央が縮み、左右に均等分配される。
    /// </summary>
    [Fact]
    public async Task FitColumn_CenterCell_TallImage_DistributesPaddingEvenly()
    {
        var (placementId, gridId) = await SeedAsync(
            assetWidth: 100, assetHeight: 200,
            cols: 3, rows: 3,
            placementCol: 1, placementRow: 1,
            scalingMode: ScalingMode.UniformContain);

        var result = await _useCase.ExecuteAsync(placementId, FitAxis.Column);
        result.IsError.Should().BeFalse();

        var grid = await _fx.GridRepository.FindByIdAsync(gridId);
        grid.Should().NotBeNull();
        var cw = grid!.ColWeights;

        // 対称性: 左右が等しい
        cw[0].Should().Be(cw[2]);
        // 中央が縮む（比率 100/600 = 1/6）
        var sum = (double)cw.Sum();
        (cw[1] / sum).Should().BeApproximately(100.0 / 600.0, 0.02);
        // 左右が広がる（比率 250/600 = 5/12）
        (cw[0] / sum).Should().BeApproximately(250.0 / 600.0, 0.02);
    }

    /// <summary>
    /// 最左セル (0,1) を占有 + 縦長画像 → 列フィットで左 pad 破棄。右隣のみ広がる。
    /// 全体合計幅は縮み、列 2 が相対的に広く見える。
    /// </summary>
    [Fact]
    public async Task FitColumn_LeftmostCell_DiscardsLeftPad()
    {
        var (placementId, gridId) = await SeedAsync(
            assetWidth: 100, assetHeight: 200,
            cols: 3, rows: 3,
            placementCol: 0, placementRow: 1,
            scalingMode: ScalingMode.UniformContain);

        var result = await _useCase.ExecuteAsync(placementId, FitAxis.Column);
        result.IsError.Should().BeFalse();

        var grid = await _fx.GridRepository.FindByIdAsync(gridId);
        var cw = grid!.ColWeights;

        // 元 200/200/200 → 列 0 内側 100、列 1 (+rightPad) 250、列 2 そのまま 200。合計 550px。
        var sum = (double)cw.Sum();
        (cw[0] / sum).Should().BeApproximately(100.0 / 550.0, 0.02);
        (cw[1] / sum).Should().BeApproximately(250.0 / 550.0, 0.02);
        (cw[2] / sum).Should().BeApproximately(200.0 / 550.0, 0.02);
    }

    /// <summary>UniformCover (余白なし) は列フィットで何も変わらない。</summary>
    [Fact]
    public async Task FitColumn_CoverMode_NoChange()
    {
        var (placementId, gridId) = await SeedAsync(
            assetWidth: 100, assetHeight: 200,
            cols: 3, rows: 3,
            placementCol: 1, placementRow: 1,
            scalingMode: ScalingMode.UniformCover);

        var grid0 = await _fx.GridRepository.FindByIdAsync(gridId);
        var before = grid0!.ColWeights;

        var result = await _useCase.ExecuteAsync(placementId, FitAxis.Column);
        result.IsError.Should().BeFalse();

        var grid1 = await _fx.GridRepository.FindByIdAsync(gridId);
        grid1!.ColWeights.SequenceEqual(before).Should().BeTrue();
    }

    /// <summary>
    /// 横長画像 (200x100) + UniformContain → 上下余白あり。行フィットで行高が縮み、上下に均等分配。
    /// </summary>
    [Fact]
    public async Task FitRow_CenterCell_WideImage_DistributesPaddingEvenly()
    {
        var (placementId, gridId) = await SeedAsync(
            assetWidth: 200, assetHeight: 100,
            cols: 3, rows: 3,
            placementCol: 1, placementRow: 1,
            scalingMode: ScalingMode.UniformContain);

        var result = await _useCase.ExecuteAsync(placementId, FitAxis.Row);
        result.IsError.Should().BeFalse();

        var grid = await _fx.GridRepository.FindByIdAsync(gridId);
        var rw = grid!.RowWeights;

        rw[0].Should().Be(rw[2]);
        var sum = (double)rw.Sum();
        (rw[1] / sum).Should().BeApproximately(100.0 / 600.0, 0.02);
    }

    /// <summary>
    /// UniformCover + PixelOffset で空白が出るケース。100x100 画像 + 200x200 セル (Cover scale=2)、
    /// PixelOffset.X=+50 で左に 50px 空白 → 列フィットで列幅 200→150 に縮み、右隣に 50 加算。
    /// </summary>
    [Fact]
    public async Task FitColumn_CoverWithPixelOffset_FitsToVisibleRect()
    {
        var (placementId, gridId) = await SeedAsync(
            assetWidth: 100, assetHeight: 100,
            cols: 3, rows: 3,
            placementCol: 1, placementRow: 1,
            scalingMode: ScalingMode.UniformCover);

        // PixelOffset を直接設定
        var placement = await _fx.PlacementRepository.FindByIdAsync(placementId);
        placement!.PixelOffsetX = 50;
        placement.PixelOffsetY = 0;
        var upd = await _fx.PlacementRepository.UpdateAsync(placement);
        upd.IsError.Should().BeFalse();

        var result = await _useCase.ExecuteAsync(placementId, FitAxis.Column);
        result.IsError.Should().BeFalse();

        var grid = await _fx.GridRepository.FindByIdAsync(gridId);
        var cw = grid!.ColWeights;

        // PixelOffset.X=+50 でセル中央 (200..400) 内の実描画矩形は (250, 200, 150, 200)。
        // leftPad = 250 - 200 = 50 → 列 0 (左隣) に +50。
        // rightPad = 400 - (250+150) = 0 → 列 2 は変化なし。
        // inner = 150 → 列 1 は 200 → 150 に縮む。
        // 結果: 列 0 = 250, 列 1 = 150, 列 2 = 200、合計 600。
        var sum = (double)cw.Sum();
        (cw[0] / sum).Should().BeApproximately(250.0 / 600.0, 0.02);
        (cw[1] / sum).Should().BeApproximately(150.0 / 600.0, 0.02);
        (cw[2] / sum).Should().BeApproximately(200.0 / 600.0, 0.02);
    }

    [Fact]
    public async Task Returns_NotFound_For_Missing_Placement()
    {
        var result = await _useCase.ExecuteAsync(Guid.NewGuid(), FitAxis.Column);
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    /// <summary>
    /// asset / copy / grid / placement を作成し、copy の ScalingMode のみ任意指定可能。
    /// グリッドは均等 [1,...,1]、CanvasSize=600x600 の cols×rows (各セル 200×200 想定)。
    /// </summary>
    private async Task<(Guid PlacementId, Guid GridId)> SeedAsync(
        int assetWidth, int assetHeight,
        int cols, int rows,
        int placementCol, int placementRow,
        ScalingMode scalingMode)
    {
        var hash = $"hash{Guid.NewGuid():N}";
        var asset = await _fx.SeedAssetAsync(hash, assetWidth, assetHeight);

        // SeedCopyAsync は ScalingMode 指定不可。直接構築して投入。
        var now = DateTimeOffset.UtcNow;
        var copy = new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            Transform = ImageTransform.Identity,
            ScalingMode = scalingMode,
            TrimmingAnchor = TrimmingAnchor.Center,
            Alignment = Alignment.Center,
            OccupySize = OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var copyResult = await _fx.CopyRepository.AddAsync(copy);
        copyResult.IsError.Should().BeFalse();

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
            CreatedAt = now,
            UpdatedAt = now,
        };
        var gridResult = await _fx.GridRepository.AddAsync(grid);
        gridResult.IsError.Should().BeFalse();

        var p = await _place.ExecuteAsync(grid.Id, copy.Id, new CellPosition(placementCol, placementRow));
        p.IsError.Should().BeFalse();
        return (p.Value.Id, grid.Id);
    }
}
