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

    // ─── ProtectedRegion セル内ステッカー方式 統合テスト ─────────────────────
    // 通常モード / PhotoBoard モード共通で、 1 region につき
    //   (a) 親側塗り: region.Rect ∩ effective Crop の可視部分を FillMode の色で塗る
    //   (b) asset 描画: 元画像 (Crop / Transform 適用前) から region.Rect を切り出し、
    //       親と同じ source→cell スケールで cell-local (OffsetXPx, OffsetYPx) に左上揃えで描画
    // を行う。 PhotoBoard 出力時は cell に焼き込んだ後にばらつき (回転 / オフセット) が適用される。

    [Fact]
    public async Task Normal_RegionFillMode_White_FillsParentWithWhiteAndDrawsAsset()
    {
        // 100×100 赤画像、 region = 中央 (0.4, 0.4, 0.2, 0.2) 源座標 = (40, 40, 20, 20) px、
        // FillMode=White、 Offset=(0,0)、 Normal モード。
        // → 親側 (40-60, 40-60) に白塗り、 asset (red 20x20) が cell TL (0-20, 0-20) に描画される。
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
        // 親側白塗り中央 (50, 50)
        rendered.GetPixel(50, 50).Should().Be(SKColors.White);
        // asset 領域 cell TL (10, 10) は asset (赤) で被覆されている
        rendered.GetPixel(10, 10).Should().Be(SKColors.Red);
        // 親領域 (asset 範囲外、 親白塗り範囲外) は赤のまま
        rendered.GetPixel(80, 20).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task Normal_RegionFillMode_Black_FillsParentWithBlack()
    {
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.4, 0.4, 0.2, 0.2), 0,
                fillMode: ProtectedRegionFillMode.Black)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.GetPixel(50, 50).Should().Be(SKColors.Black);
    }

    [Fact]
    public async Task Normal_RegionFillMode_Transparent_PunchesAlphaHoleInParent()
    {
        // FillMode=Transparent は親側の region 領域の alpha を 0 にする (穴を開ける)。
        // Normal モードのキャンバスは初期 Transparent なので、 出力 PNG の region 中央は透明になる。
        // Offset を画像端に振って asset が中央を上書きしないようにする (検証は親側塗りに集中)。
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.4, 0.4, 0.2, 0.2), 0,
                fillMode: ProtectedRegionFillMode.Transparent,
                offsetXPx: 80, offsetYPx: 80)));  // asset を右下隅に追いやる
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 中央 (50, 50) は親側塗りで alpha=0 に punch されている
        rendered.GetPixel(50, 50).Alpha.Should().Be(0);
        // 親側塗り範囲外 (10, 10) は赤のまま
        rendered.GetPixel(10, 10).Should().Be(SKColors.Red);
    }

    [Fact]
    public async Task Normal_RegionFillMode_Custom_UsesProvidedColor()
    {
        // 0xFF008000 = 不透明の濃い緑
        var customColor = 0xFF_00_80_00u;
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.4, 0.4, 0.2, 0.2), 0,
                fillMode: ProtectedRegionFillMode.Custom,
                fillColor: customColor)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.GetPixel(50, 50).Should().Be(new SKColor(customColor));
    }

    [Fact]
    public async Task Normal_RegionOffset_PositionsAssetAtSpecifiedPixel()
    {
        // 左半分赤・右半分青の 100×100 画像、 region = 左上 (0.0, 0.0, 0.2, 0.2) 源座標 = (0,0,20,20) → 赤。
        // Offset=(50, 60) → asset は cell の (50, 60) を左上として 20×20 の赤矩形が描かれる。
        var imagePath = WriteHalfSplitPng(100, 100, SKColors.Red, SKColors.Blue);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 0.2, 0.2), 0,
                offsetXPx: 50, offsetYPx: 60)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // asset 中央 (60, 70) は赤 (元画像左半分の slice)
        rendered.GetPixel(60, 70).Should().Be(SKColors.Red);
        // asset 範囲外の右下 (80, 80) は元画像の右下 (青) のまま
        rendered.GetPixel(80, 80).Should().Be(SKColors.Blue);
    }

    [Fact]
    public async Task Region_AssetIsIndependentOfCrop()
    {
        // ManualCrop = 右半分のみ表示。 region.Rect = 元画像の左上 (Crop 外) を切り出して
        // asset として描画。 Crop 非依存仕様により asset 自体は描画され、 親側塗りは Crop 外なので発生しない。
        var imagePath = WriteHalfSplitPng(100, 100, SKColors.Red, SKColors.Blue);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(
            scaling: ScalingMode.Fill,
            manualCrop: new ManualCropFraction(0.5, 0.0, 0.5, 1.0),  // 右半分=青のみ表示
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 0.3, 0.3), 0,
                offsetXPx: 0, offsetYPx: 0)));  // asset を cell TL に
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 親 (Crop 後右半分=青) が cell 全面に Fill で拡大 → 全面青 が前提
        // asset (元画像左上=赤の 30x30 source pixel) が描画される (Crop 非依存)
        // → cell TL 付近に赤ピクセルが存在 (asset)、 親側塗りは Crop 外なので白塗り無し
        rendered.GetPixel(5, 5).Should().Be(SKColors.Red);    // asset
        rendered.GetPixel(95, 95).Should().Be(SKColors.Blue); // 親 (Crop 内 = 青)
        // 出力全面に白ピクセルが存在しないこと (region は Crop 外なので親側塗りされない)
        var hasWhitePixel = false;
        for (int y = 0; y < rendered.Height && !hasWhitePixel; y++)
            for (int x = 0; x < rendered.Width; x++)
                if (rendered.GetPixel(x, y) == SKColors.White) { hasWhitePixel = true; break; }
        hasWhitePixel.Should().BeFalse("region が effective Crop の外なので親側塗りされないこと");
    }

    [Fact]
    public async Task PhotoBoard_RegionAsset_IsBakedIntoCellAndFollowsParentRotation()
    {
        // PhotoBoard ばらつき (Off 係数で回転 0) では cell 内合成結果がそのまま canvas に焼かれる。
        // → Normal と同じ位置 (cell TL) に asset が現れる。
        var imagePath = WriteHalfSplitPng(100, 100, SKColors.Red, SKColors.Blue);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 0.2, 0.2), 0,
                offsetXPx: 0, offsetYPx: 0)));
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
        // PhotoBoard.Off 係数では cell が canvas にそのまま貼られる (rotation=0, offset=0)。
        // asset (左上 20x20 = 赤) が cell TL に焼き込まれて canvas 上にも赤として現れる。
        rendered.GetPixel(5, 5).Should().Be(SKColors.Red);
    }

    // ─── ProtectedRegion 独立 transform (Rotation / FlipX / FlipY) 統合テスト ───
    // 100x100 4 象限画像 (TL=Red, TR=Lime, BL=Blue, BR=Yellow) を全域 region で配置し、
    // ScalingMode=Fill / 親 Transform=Identity / cell=100x100 で source→cell が 1:1。
    // region 自身の Rotation/Flip を変えたとき、 各象限の色が正しく入れ替わることを検証。

    [Fact]
    public async Task Region_Rotation_Cw90_RotatesAssetClockwise()
    {
        var imagePath = WriteQuadrantPng(100, 100, SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.Fill,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 1.0, 1.0), 0,
                rotation: Rotation.Cw90)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // Cw90 では元 TL→canvas TR、 元 TR→canvas BR、 元 BR→canvas BL、 元 BL→canvas TL。
        rendered.GetPixel(75, 25).Should().Be(SKColors.Red);    // 元 TL → canvas TR
        rendered.GetPixel(75, 75).Should().Be(SKColors.Lime);   // 元 TR → canvas BR
        rendered.GetPixel(25, 75).Should().Be(SKColors.Yellow); // 元 BR → canvas BL
        rendered.GetPixel(25, 25).Should().Be(SKColors.Blue);   // 元 BL → canvas TL
    }

    [Fact]
    public async Task Region_Rotation_Cw180_RotatesAssetHalfTurn()
    {
        var imagePath = WriteQuadrantPng(100, 100, SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.Fill,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 1.0, 1.0), 0,
                rotation: Rotation.Cw180)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // Cw180 で対角入れ替え: TL ↔ BR、 TR ↔ BL
        rendered.GetPixel(75, 75).Should().Be(SKColors.Red);    // 元 TL → canvas BR
        rendered.GetPixel(25, 75).Should().Be(SKColors.Lime);   // 元 TR → canvas BL
        rendered.GetPixel(75, 25).Should().Be(SKColors.Blue);   // 元 BL → canvas TR
        rendered.GetPixel(25, 25).Should().Be(SKColors.Yellow); // 元 BR → canvas TL
    }

    [Fact]
    public async Task Region_FlipX_MirrorsAssetHorizontally()
    {
        var imagePath = WriteQuadrantPng(100, 100, SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.Fill,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 1.0, 1.0), 0,
                flipX: true)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // FlipX で左右入れ替え: TL ↔ TR、 BL ↔ BR
        rendered.GetPixel(75, 25).Should().Be(SKColors.Red);    // 元 TL → canvas TR
        rendered.GetPixel(25, 25).Should().Be(SKColors.Lime);   // 元 TR → canvas TL
        rendered.GetPixel(75, 75).Should().Be(SKColors.Blue);   // 元 BL → canvas BR
        rendered.GetPixel(25, 75).Should().Be(SKColors.Yellow); // 元 BR → canvas BL
    }

    [Fact]
    public async Task Region_FlipY_MirrorsAssetVertically()
    {
        var imagePath = WriteQuadrantPng(100, 100, SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.Fill,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.0, 0.0, 1.0, 1.0), 0,
                flipY: true)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // FlipY で上下入れ替え: TL ↔ BL、 TR ↔ BR
        rendered.GetPixel(25, 75).Should().Be(SKColors.Red);    // 元 TL → canvas BL
        rendered.GetPixel(75, 75).Should().Be(SKColors.Lime);   // 元 TR → canvas BR
        rendered.GetPixel(25, 25).Should().Be(SKColors.Blue);   // 元 BL → canvas TL
        rendered.GetPixel(75, 25).Should().Be(SKColors.Yellow); // 元 BR → canvas TR
    }

    [Fact]
    public async Task Region_Rotation_DoesNotAffectParentFillRectangle()
    {
        // region.Rotation は asset 描画にのみ作用し、 親側塗り (region.Rect ベース) には無関係。
        // 100x100 赤画像、 region = (0.4, 0.4, 0.2, 0.2)、 FillMode=White、 Cw90 + FlipX。
        // 親側塗りは元の rect (40-60, 40-60) のまま、 asset は rotation/flip 後の位置に描画される。
        // Offset=(80, 80) で asset を右下に追いやり、 親側塗り判定だけを中央で確認する。
        var imagePath = WriteSolidColorPng(100, 100, SKColors.Red);
        var grid = CreateGrid(rows: 1, cols: 1, canvas: new PixelSize(100, 100));
        var copy = CreateCopyWithRegions(scaling: ScalingMode.None,
            regions: ImmutableArray.Create(MakeRegion(
                new RegionRectFraction(0.4, 0.4, 0.2, 0.2), 0,
                fillMode: ProtectedRegionFillMode.White,
                offsetXPx: 80, offsetYPx: 80,
                rotation: Rotation.Cw90,
                flipX: true)));
        var placement = CreatePlacement(grid.Id, copy.Id, new CellPosition(0, 0));

        var result = await _renderer.RenderPngAsync(
            grid, [new PlacementRenderItem(placement, copy, imagePath)],
            new RenderOptions(TrimMode: TrimMode.None, OutputMode: OutputMode.Normal));

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        // 中央 (50, 50) は親側塗りで白 (region.Rotation/Flip は影響しない)
        rendered.GetPixel(50, 50).Should().Be(SKColors.White);
        // 親領域外 (10, 10) は赤のまま
        rendered.GetPixel(10, 10).Should().Be(SKColors.Red);
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
                FillColor = r.FillColor,
                OffsetXPx = r.OffsetXPx,
                OffsetYPx = r.OffsetYPx,
                Rotation = r.Rotation,
                FlipX = r.FlipX,
                FlipY = r.FlipY,
                SortOrder = r.SortOrder,
            }).ToImmutableArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProtectedRegion MakeRegion(
        RegionRectFraction rect, int sortOrder,
        ProtectedRegionFillMode fillMode = ProtectedRegionFillMode.White,
        uint? fillColor = null,
        int offsetXPx = 0,
        int offsetYPx = 0,
        Rotation rotation = Rotation.None,
        bool flipX = false,
        bool flipY = false) => new()
    {
        Id = Guid.NewGuid(),
        ImageCopyId = Guid.Empty,  // CreateCopyWithRegions で copyId に差し替えられる
        Rect = rect,
        FillMode = fillMode,
        FillColor = fillColor,
        OffsetXPx = offsetXPx,
        OffsetYPx = offsetYPx,
        Rotation = rotation,
        FlipX = flipX,
        FlipY = flipY,
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

    /// <summary>
    /// 4 象限を異なる単色で塗った PNG を生成する。 region の rotation/flip 効果検証用。
    /// 象限: TL=topLeft, TR=topRight, BL=bottomLeft, BR=bottomRight。
    /// </summary>
    private string WriteQuadrantPng(int w, int h, SKColor tl, SKColor tr, SKColor bl, SKColor br)
    {
        using var bitmap = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = false };
            paint.Color = tl;
            canvas.DrawRect(0, 0, w / 2, h / 2, paint);
            paint.Color = tr;
            canvas.DrawRect(w / 2, 0, w / 2, h / 2, paint);
            paint.Color = bl;
            canvas.DrawRect(0, h / 2, w / 2, h / 2, paint);
            paint.Color = br;
            canvas.DrawRect(w / 2, h / 2, w / 2, h / 2, paint);
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
