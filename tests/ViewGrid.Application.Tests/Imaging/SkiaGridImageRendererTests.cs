using System.Collections.Immutable;
using FluentAssertions;
using SkiaSharp;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;

namespace ViewGrid.Application.Tests.Imaging;

public sealed class SkiaGridImageRendererTests : IAsyncLifetime
{
    private DirectoryInfo _tempDir = null!;
    private SkiaGridImageRenderer _renderer = null!;

    public Task InitializeAsync()
    {
        _tempDir = TestImageFactory.CreateTempDirectory();
        _renderer = new SkiaGridImageRenderer(new AutoCropCache());
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_tempDir.Exists)
            _tempDir.Delete(recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Renders_Solid_Color_Filling_Whole_Canvas_When_One_Image_Covers_Single_Cell()
    {
        var imagePath = WriteSolidColorPng(64, 64, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy();
        var placement = CreatePlacement(grid.Id, copy.Id, position: new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(100);
        rendered.Height.Should().Be(100);
        rendered.GetPixel(50, 50).Should().Be(SKColors.Red);
        rendered.GetPixel(0, 0).Should().Be(SKColors.Red);
        rendered.GetPixel(99, 99).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task Two_By_Two_Grid_Places_Each_Color_In_Its_Quadrant()
    {
        var redPath = WriteSolidColorPng(50, 50, SKColors.Red);
        var greenPath = WriteSolidColorPng(50, 50, SKColors.Lime);
        var bluePath = WriteSolidColorPng(50, 50, SKColors.Blue);
        var yellowPath = WriteSolidColorPng(50, 50, SKColors.Yellow);

        var grid = CreateGrid(rows: 2, cols: 2, canvas: new PixelSize(100, 100));
        var copy = CreateCopy();
        var items = new List<PlacementRenderItem>
        {
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 0), copy, redPath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 0), order: 1), copy, greenPath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 1), order: 2), copy, bluePath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 1), order: 3), copy, yellowPath),
        };

        var result = await _renderer.RenderPngAsync(grid, items, RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.GetPixel(25, 25).Should().Be(SKColors.Red);
        rendered.GetPixel(75, 25).Should().Be(SKColors.Lime);
        rendered.GetPixel(25, 75).Should().Be(SKColors.Blue);
        rendered.GetPixel(75, 75).Should().Be(SKColors.Yellow);
    }

    [Fact]
    public async Task Free_Scaling_With_Wide_Image_Leaves_Transparent_Padding_Top_And_Bottom()
    {
        // 横長（200x50）を正方形セル（100x100）に Free + Center で配置 → 中央 100x25 が画像、上下は透明。
        var imagePath = WriteSolidColorPng(200, 50, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy(scaling: ScalingMode.UniformContain, alignment: Alignment.Center);
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 中央付近は画像（赤）
        rendered.GetPixel(50, 50).Should().Be(SKColors.Red);
        // 上端・下端は透明
        rendered.GetPixel(50, 5).Alpha.Should().Be(0);
        rendered.GetPixel(50, 95).Alpha.Should().Be(0);
    }

    [Fact]
    public async Task Fixed_Scaling_With_Larger_Image_Crops_To_Cell_Using_Alignment()
    {
        // 200x200 のソース（左半分赤・右半分青）を 100x100 のセルに None + TopLeft で配置
        // → ソースの (0,0)-(100,100) が出力される（=全部赤）。
        // Alignment 単一アンカーで「画像 > セル」のトリミング側も決まる。
        var imagePath = WriteHalfSplitPng(200, 200, SKColors.Red, SKColors.Blue);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy(scaling: ScalingMode.None, alignment: new Alignment(AnchorX.Left, AnchorY.Top));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.GetPixel(50, 50).Should().Be(SKColors.Red);
        rendered.GetPixel(99, 99).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task Higher_Placement_Order_Draws_On_Top()
    {
        // 同じ位置に 2 枚の placement（Validator は通常防ぐが、renderer は order 順にそのまま描く）
        var redPath = WriteSolidColorPng(50, 50, SKColors.Red);
        var greenPath = WriteSolidColorPng(50, 50, SKColors.Lime);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(50, 50));
        var copy = CreateCopy();
        var items = new List<PlacementRenderItem>
        {
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 0), copy, redPath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 1), copy, greenPath),
        };

        var result = await _renderer.RenderPngAsync(grid, items, RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.GetPixel(25, 25).Should().Be(SKColors.Lime);
    }

    [Fact]
    public async Task UniformCover_With_Wide_Image_Fills_Cell_And_Crops_Sides()
    {
        // 横長（200x50）を 1×1 / 100x100 のセルに UniformCover + Center で配置
        // → アスペクト維持で高さ 100 を埋める scale=2.0、横は 400 に拡大して左右 150 ずつトリミング。
        //    可視 src 範囲は中央 50px、出力は dst 全面が画像で埋まる。
        var imagePath = WriteHalfSplitPng(200, 50, SKColors.Red, SKColors.Blue);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy(scaling: ScalingMode.UniformCover, alignment: Alignment.Center);
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 左半分は赤・右半分は青（src の中央 50px を引き伸ばし、左半分が src 左端の赤、右半分が src 右端の青）
        rendered.GetPixel(25, 50).Should().Be(SKColors.Red);
        rendered.GetPixel(75, 50).Should().Be(SKColors.Blue);
        // 上端・下端も透明ではなく画像
        rendered.GetPixel(50, 5).Alpha.Should().Be(255);
        rendered.GetPixel(50, 95).Alpha.Should().Be(255);
    }

    [Fact]
    public async Task Fill_Stretches_Anisotropically_To_Cover_Cell()
    {
        // 横長（200x50）を 1×1 / 100x100 のセルに Fill で配置 → アスペクト破壊で全面を埋める。
        var imagePath = WriteSolidColorPng(200, 50, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy(scaling: ScalingMode.Fill);
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 全ピクセルが赤で埋まる（透明部分なし）
        rendered.GetPixel(50, 5).Should().Be(SKColors.Red);
        rendered.GetPixel(50, 50).Should().Be(SKColors.Red);
        rendered.GetPixel(50, 95).Should().Be(SKColors.Red);
        rendered.GetPixel(2, 50).Should().Be(SKColors.Red);
        rendered.GetPixel(98, 50).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task PixelOffset_Is_Clipped_At_Cell_Boundary_So_Adjacent_Cell_Stays_Transparent()
    {
        // 2×1 グリッドの (0,0) に赤画像を配置し、ΔY=-50 で上に動かす。
        // 上隣のセルは存在しないが、(0,0) のセル領域 (0..50, 0..50) を超えた y=-50..0 への描画は
        // クリップされるため、出力画像 (0..50, 0..50) は全面赤で、(50..100, 0..50) は透明のまま。
        // 隣セル (1,0) との境界が侵食されないことを確認する。
        var redPath = WriteSolidColorPng(50, 50, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 2, canvas: new PixelSize(100, 50));
        var copy = CreateCopy(scaling: ScalingMode.None);
        var placement = CreatePlacementWithOffset(grid.Id, copy.Id, new CellPosition(0, 0), pxOffsetX: 30, pxOffsetY: 0);

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, redPath)], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // (0,0) のセル右半分は赤（PixelOffset で動いた画像が見える範囲）
        rendered.GetPixel(40, 25).Should().Be(SKColors.Red);
        // 隣セル (1,0) の領域は透明のまま（PixelOffset で動いた画像はセル境界でクリップ）
        rendered.GetPixel(60, 25).Alpha.Should().Be(0);
        rendered.GetPixel(75, 25).Alpha.Should().Be(0);
    }

    [Fact]
    public async Task Returns_NotFound_When_Source_Image_Missing()
    {
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy();
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));
        var missingPath = Path.Combine(_tempDir.FullName, "missing.png");

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, missingPath)], RenderOptions.Default);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Empty_Placements_Returns_Transparent_Canvas_Of_Specified_Size()
    {
        var grid = CreateGrid(rows: 3, cols: 4, canvas: new PixelSize(80, 60));

        var result = await _renderer.RenderPngAsync(grid, [], RenderOptions.Default);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(80);
        rendered.Height.Should().Be(60);
        rendered.GetPixel(40, 30).Alpha.Should().Be(0);
    }

    [Fact]
    public async Task TrimMode_None_Returns_Full_Canvas_Size()
    {
        // 3×3 グリッドで左上 1×1 のみ占有 → None なら 120×120 全面
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 3, cols: 3, canvas: new PixelSize(120, 120));
        var copy = CreateCopy();
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode.None));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(120);
        rendered.Height.Should().Be(120);
    }

    [Fact]
    public async Task TrimMode_OccupiedCells_Crops_To_Cell_Bounding_Box()
    {
        // 3×3 グリッドで (0,0) と (1,0) のみ占有 → 占有 bbox は左上 80×40
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 3, cols: 3, canvas: new PixelSize(120, 120));
        var copy = CreateCopy();
        var items = new List<PlacementRenderItem>
        {
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 0), copy, imagePath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 0), order: 1), copy, imagePath),
        };

        var result = await _renderer.RenderPngAsync(grid, items, new RenderOptions(TrimMode.OccupiedCells));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(80);
        rendered.Height.Should().Be(40);
        // 中央が赤いことを確認（占有セル内の描画ピクセル）
        rendered.GetPixel(20, 20).Should().Be(SKColors.Red);
        rendered.GetPixel(60, 20).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task TrimMode_DrawnPixels_Crops_To_Drawn_Pixel_Bounding_Box()
    {
        // 3×3 グリッドの (1,1) に 40×40 の赤画像を Stretch.None で配置 → 中央セル 40×40 描画。
        // セル枠 ((40,40)-(80,80)) の内側 40×40 に赤、それ以外は透過 → bbox は 40×40
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 3, cols: 3, canvas: new PixelSize(120, 120));
        var copy = CreateCopy(scaling: ScalingMode.None);
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 1));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode.DrawnPixels));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(40);
        rendered.Height.Should().Be(40);
        // すべての角が赤（透過なし）
        rendered.GetPixel(0, 0).Should().Be(SKColors.Red);
        rendered.GetPixel(39, 39).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task TrimMode_DrawnPixels_Removes_Subpixel_Transparent_Fringe_From_Contained_Image()
    {
        // 横長画像を正方形セルへ Contain + Center で配置すると dstY=37.5 になり、
        // Skia の線形補間で上外周に薄い半透明行が出る。DrawnPixels はそれを余白として落とす。
        var imagePath = WriteSolidColorPng(200, 50, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopy(scaling: ScalingMode.UniformContain, alignment: Alignment.Center);
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode.DrawnPixels));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(100);
        rendered.Height.Should().Be(25);
        rendered.GetPixel(50, 0).Should().Be(SKColors.Red);
        rendered.GetPixel(50, 24).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task TrimMode_DrawnPixels_Does_Not_Extend_Beyond_Occupied_Cells()
    {
        // 占有セル外の領域に α が漏れていても DrawnPixels bbox はその外に拡張しないことを検証する。
        // 3×3 グリッドで右下 (2,2) のみ占有。占有セル bbox は (80,80)-(120,120) の 40×40。
        // 画像は 40×40 の赤を Stretch.None で配置 → セル全体を覆う → 描画 bbox は 40×40
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 3, cols: 3, canvas: new PixelSize(120, 120));
        var copy = CreateCopy(scaling: ScalingMode.None);
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(2, 2));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode.DrawnPixels));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(40);
        rendered.Height.Should().Be(40);
        rendered.GetPixel(0, 0).Should().Be(SKColors.Red);
        rendered.GetPixel(39, 39).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task TrimMode_OccupiedCells_With_No_Placements_Returns_Tiny_Transparent_Image()
    {
        // 配置なしで OccupiedCells は bbox=空 → ファイル破損を避けるため 1×1 透過画像を返す
        var grid = CreateGrid(rows: 3, cols: 3, canvas: new PixelSize(120, 120));

        var result = await _renderer.RenderPngAsync(grid, [], new RenderOptions(TrimMode.OccupiedCells));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(1);
        rendered.Height.Should().Be(1);
        rendered.GetPixel(0, 0).Alpha.Should().Be(0);
    }

    // ─── PhotoBoard モード統合テスト ───

    [Fact]
    public async Task PhotoBoard_Off_Coefficients_Produces_Same_Dimensions_As_Normal_None()
    {
        // OutputMode.PhotoBoard + Off 係数 (フレーム / シャドウすら 0) は
        // マージン 0 で出力され、 OutputMode.Normal と同じキャンバスサイズになる。
        // (バイト同一性まで保証する必要はない: PhotoBoard 経路は中間バッファを通るため
        //  Skia の内部レンダリング順序で微差が出る可能性あり。 視覚同等性は別途確認)
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 2, cols: 2, canvas: new PixelSize(100, 100));
        var copy = CreateCopy();
        var items = new List<PlacementRenderItem>
        {
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 0), copy, imagePath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 1), order: 1), copy, imagePath),
        };

        var photoBoardResult = await _renderer.RenderPngAsync(
            grid, items,
            new RenderOptions(
                TrimMode: TrimMode.None,
                OutputMode: OutputMode.PhotoBoard,
                PhotoBoardCoefficients: PhotoBoardStyleCoefficients.Off,
                PhotoBoardSeedOverride: 12345));
        var noneResult = await _renderer.RenderPngAsync(
            grid, items,
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        photoBoardResult.IsError.Should().BeFalse();
        noneResult.IsError.Should().BeFalse();
        using var photoBoardImage = SKBitmap.Decode(photoBoardResult.Value);
        using var noneImage = SKBitmap.Decode(noneResult.Value);
        photoBoardImage.Width.Should().Be(noneImage.Width);
        photoBoardImage.Height.Should().Be(noneImage.Height);
    }

    [Fact]
    public async Task PhotoBoard_Scattered_Max_Returns_Image_Larger_Than_Canvas()
    {
        // Scattered + intensity=1 では拡張 + マージンでキャンバスより大きい画像になる
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 2, cols: 2, canvas: new PixelSize(200, 200));
        var copy = CreateCopy();
        var items = new List<PlacementRenderItem>
        {
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 0), copy, imagePath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 1), order: 1), copy, imagePath),
        };

        var result = await _renderer.RenderPngAsync(
            grid, items,
            new RenderOptions(
                TrimMode: TrimMode.None,
                OutputMode: OutputMode.PhotoBoard,
                PhotoBoardCoefficients: PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0),
                PhotoBoardSeedOverride: 42));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().BeGreaterThan(200);
        rendered.Height.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task PhotoBoard_Same_Seed_Produces_Same_Bytes()
    {
        // シード固定で 2 回呼べば完全に同一バイト列 (再現性の UX 契約)
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 2, cols: 2, canvas: new PixelSize(200, 200));
        var copy = CreateCopy();
        var items = new List<PlacementRenderItem>
        {
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0), order: 0), copy, imagePath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 1), order: 1), copy, imagePath),
            new(CreatePlacement(grid.Id, copy.Id, new CellPosition(1, 0), order: 2), copy, imagePath),
        };
        var options = new RenderOptions(
            TrimMode: TrimMode.None,
            OutputMode: OutputMode.PhotoBoard,
            PhotoBoardCoefficients: PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Rough, 0.7),
            PhotoBoardSeedOverride: 99);

        var first = await _renderer.RenderPngAsync(grid, items, options);
        var second = await _renderer.RenderPngAsync(grid, items, options);

        first.IsError.Should().BeFalse();
        second.IsError.Should().BeFalse();
        first.Value.Should().Equal(second.Value);
    }

    [Fact]
    public async Task PhotoBoard_Has_Frame_Color_Pixel()
    {
        // PhotoBoard 出力にはフレーム色 #FAFAF8 のピクセルが現れる
        var imagePath = WriteSolidColorPng(40, 40, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(200, 200));
        var copy = CreateCopy();
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(
                TrimMode: TrimMode.None,
                OutputMode: OutputMode.PhotoBoard,
                PhotoBoardCoefficients: PhotoBoardStyleCoefficients.For(PhotoBoardStyle.Scattered, 1.0),
                PhotoBoardSeedOverride: 7));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);

        // フレーム色 (#FAFAF8) のピクセルが少なくとも 1 つは存在する
        // 12px の側面フレームなので画像周辺をスキャン
        var frameColor = new SKColor(0xFA, 0xFA, 0xF8);
        bool hasFramePixel = false;
        for (int y = 0; y < rendered.Height && !hasFramePixel; y++)
        {
            for (int x = 0; x < rendered.Width; x++)
            {
                if (rendered.GetPixel(x, y) == frameColor)
                {
                    hasFramePixel = true;
                    break;
                }
            }
        }
        hasFramePixel.Should().BeTrue("ポラロイド風フレーム (#FAFAF8) のピクセルが出力に現れること");
    }

    // ─── ProtectedRegion 白塗りテスト (Phase 1 step 6) ─────────────────
    // PhotoBoard 経路のみで cell-bounded image に白塗りされる。 PhotoBoard.Off 係数
    // (jitter / rotation すべて 0) を使い、 ピクセル位置がほぼ Normal と同じに保たれる
    // 状況で region の白塗り位置を確認する (overlay は step 7 で別途実装するため、
    // 本セットのテストでは 「白い穴」 が観察できることを期待値として書く)。

    [Fact]
    public async Task PhotoBoard_PaintsWhite_AtRegionPosition_WhenRegionInsideImage()
    {
        // 100×100 赤画像、 100×100 セル、 region = 中央 (0.4, 0.4, 0.2, 0.2) → 20×20 白塗り
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(new RegionRectFraction(0.4, 0.4, 0.2, 0.2), 0)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(
                TrimMode: TrimMode.None,
                OutputMode: OutputMode.PhotoBoard,
                PhotoBoardCoefficients: PhotoBoardStyleCoefficients.Off,
                PhotoBoardSeedOverride: 0));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // セル中央 (50, 50) は region 内部 → 白
        rendered.GetPixel(50, 50).Should().Be(SKColors.White);
        // 中央付近の region 内 (45, 45)〜(55, 55) も白 (region は (40,40)-(60,60))
        rendered.GetPixel(45, 45).Should().Be(SKColors.White);
        rendered.GetPixel(55, 55).Should().Be(SKColors.White);
        // region 外 (10, 10) / (90, 90) は元の赤
        rendered.GetPixel(10, 10).Should().Be(SKColors.Red);
        rendered.GetPixel(90, 90).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task PhotoBoard_DoesNotPaintWhite_WhenRegionOutsideEffectiveCrop()
    {
        // ManualCrop = 右半分 (0.5, 0, 0.5, 1) を Fill で 100×100 セル全面に拡大、
        // region = 元画像の左上 (0, 0, 0.3, 0.3) → 完全に Crop 外 → 白塗りされない。
        // (ScalingMode.Fill で cell 全面が画像で埋まり、 cell 背景の白が露出しない状況にして
        // 「白塗りなし = 全面赤」 を厳密に確認できる)
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(
            scaling: ScalingMode.Fill,
            manualCrop: new ManualCropFraction(0.5, 0.0, 0.5, 1.0),
            regions: ImmutableArray.Create(MakeRegion(new RegionRectFraction(0.0, 0.0, 0.3, 0.3), 0)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(
                TrimMode: TrimMode.None,
                OutputMode: OutputMode.PhotoBoard,
                PhotoBoardCoefficients: PhotoBoardStyleCoefficients.Off,
                PhotoBoardSeedOverride: 0));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 出力全面に白ピクセルが存在しないこと (region は Crop 外なので白塗りされない)
        var hasWhitePixel = false;
        for (int y = 0; y < rendered.Height && !hasWhitePixel; y++)
            for (int x = 0; x < rendered.Width; x++)
                if (rendered.GetPixel(x, y) == SKColors.White) { hasWhitePixel = true; break; }
        hasWhitePixel.Should().BeFalse("region が effective Crop の外なので白塗りされないこと");
    }

    [Fact]
    public async Task NormalMode_IgnoresRegions()
    {
        // OutputMode.Normal では Regions を完全無視 (Phase 1 は PhotoBoard 限定)
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(new RegionRectFraction(0.4, 0.4, 0.2, 0.2), 0)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // Normal モードでは region が完全無視され、 全面赤
        rendered.GetPixel(50, 50).Should().Be(SKColors.Red);
        rendered.GetPixel(45, 45).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task PhotoBoard_PaintsWhite_ForMultipleRegions()
    {
        // 2 個の region がそれぞれ独立して白塗りされる
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(
            scaling: ScalingMode.None,
            regions: ImmutableArray.Create(
                MakeRegion(new RegionRectFraction(0.0, 0.0, 0.2, 0.2), 0),  // 左上
                MakeRegion(new RegionRectFraction(0.7, 0.7, 0.2, 0.2), 1))); // 右下
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(
                TrimMode: TrimMode.None,
                OutputMode: OutputMode.PhotoBoard,
                PhotoBoardCoefficients: PhotoBoardStyleCoefficients.Off,
                PhotoBoardSeedOverride: 0));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 左上 region 内 (10, 10)
        rendered.GetPixel(10, 10).Should().Be(SKColors.White);
        // 右下 region 内 (80, 80) 程度
        rendered.GetPixel(80, 80).Should().Be(SKColors.White);
        // どちらの region にも属さない中央 (50, 50) は赤
        rendered.GetPixel(50, 50).Should().Be(SKColors.Red);
    }

    private static ImageCopy CreateCopyWithRegions(
        ScalingMode scaling = ScalingMode.UniformContain,
        Alignment? alignment = null,
        ImageTransform? transform = null,
        OccupySize? occupy = null,
        ManualCropFraction? manualCrop = null,
        ImmutableArray<ProtectedRegion>? regions = null)
    {
        var copyId = Guid.NewGuid();
        return new ImageCopy
        {
            Id = copyId,
            AssetId = Guid.NewGuid(),
            Transform = transform ?? ImageTransform.Identity,
            ScalingMode = scaling,
            Alignment = alignment ?? Alignment.Center,
            OccupySize = occupy ?? OccupySize.OneByOne,
            ManualCrop = manualCrop,
            Regions = (regions ?? ImmutableArray<ProtectedRegion>.Empty).Select(r => new ProtectedRegion
            {
                Id = r.Id,
                ImageCopyId = copyId,  // Test ImageCopy.Id に紐付け直し
                Rect = r.Rect,
                FillMode = r.FillMode,
                SortOrder = r.SortOrder,
            }).ToImmutableArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProtectedRegion MakeRegion(RegionRectFraction rect, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        ImageCopyId = Guid.Empty,  // CreateCopyWithRegions で copyId に差し替えられる
        Rect = rect,
        FillMode = ProtectedRegionFillMode.White,
        SortOrder = sortOrder,
    };

    private string WriteSolidColorPng(int w, int h, SKColor color)
    {
        var path = Path.Combine(_tempDir.FullName, $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, TestImageFactory.CreatePng(w, h, color));
        return path;
    }

    private string WriteHalfSplitPng(int w, int h, SKColor leftColor, SKColor rightColor)
    {
        using var bitmap = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint();
            paint.Color = leftColor;
            canvas.DrawRect(0, 0, w / 2, h, paint);
            paint.Color = rightColor;
            canvas.DrawRect(w / 2, 0, w / 2, h, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(_tempDir.FullName, $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, encoded.ToArray());
        return path;
    }

    private static GridCanvas CreateGrid(int rows, int cols, PixelSize canvas) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        GridRows = rows,
        GridCols = cols,
        ColWeights = GridCanvas.UniformWeights(cols),
        RowWeights = GridCanvas.UniformWeights(rows),
        CanvasSize = canvas,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static ImageCopy CreateCopy(
        ScalingMode scaling = ScalingMode.UniformContain,
        Alignment? alignment = null,
        ImageTransform? transform = null,
        OccupySize? occupy = null) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Transform = transform ?? ImageTransform.Identity,
        ScalingMode = scaling,
        Alignment = alignment ?? Alignment.Center,
        OccupySize = occupy ?? OccupySize.OneByOne,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static GridPlacement CreatePlacement(
        Guid gridId, Guid copyId, CellPosition position, int order = 0,
        OccupySize? occupy = null) => new()
    {
        Id = Guid.NewGuid(),
        GridId = gridId,
        CopyId = copyId,
        Position = position,
        OccupySize = occupy ?? OccupySize.OneByOne,
        PlacementOrder = order,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static GridPlacement CreatePlacementWithOffset(
        Guid gridId, Guid copyId, CellPosition position,
        int pxOffsetX, int pxOffsetY, int order = 0,
        OccupySize? occupy = null) => new()
    {
        Id = Guid.NewGuid(),
        GridId = gridId,
        CopyId = copyId,
        Position = position,
        OccupySize = occupy ?? OccupySize.OneByOne,
        PixelOffsetX = pxOffsetX,
        PixelOffsetY = pxOffsetY,
        PlacementOrder = order,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
