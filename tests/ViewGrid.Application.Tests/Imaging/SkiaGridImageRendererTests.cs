using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using SkiaSharp;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;
using ViewGrid.Infrastructure.Imaging;
using Xunit;

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)]);

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

        var result = await _renderer.RenderPngAsync(grid, items);

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)]);

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)]);

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

        var result = await _renderer.RenderPngAsync(grid, items);

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)]);

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, imagePath)]);

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, redPath)]);

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

        var result = await _renderer.RenderPngAsync(grid, [new PlacementRenderItem(placement, copy, missingPath)]);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
    }

    [Fact]
    public async Task Empty_Placements_Returns_Transparent_Canvas_Of_Specified_Size()
    {
        var grid = CreateGrid(rows: 3, cols: 4, canvas: new PixelSize(80, 60));

        var result = await _renderer.RenderPngAsync(grid, []);

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
            TrimMode.None);

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

        var result = await _renderer.RenderPngAsync(grid, items, TrimMode.OccupiedCells);

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
            TrimMode.DrawnPixels);

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
            TrimMode.DrawnPixels);

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
            TrimMode.DrawnPixels);

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

        var result = await _renderer.RenderPngAsync(grid, [], TrimMode.OccupiedCells);

        result.IsError.Should().BeFalse();
        using var rendered = SKBitmap.Decode(result.Value);
        rendered.Width.Should().Be(1);
        rendered.Height.Should().Be(1);
        rendered.GetPixel(0, 0).Alpha.Should().Be(0);
    }

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
            using var paint = new SKPaint { Color = leftColor };
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
        IsActive = true,
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
