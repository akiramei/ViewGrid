using System;
using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.UseCases;
using Xunit;

namespace ViewGrid.Core.Tests.UseCases;

public sealed class PlacementGeometryTests
{
    private static ImageCopy MakeCopy(
        ScalingMode mode = ScalingMode.UniformContain,
        OccupySize? occupy = null,
        Rotation rotation = Rotation.None,
        AnchorX alignX = AnchorX.Center,
        AnchorY alignY = AnchorY.Center,
        AnchorX trimX = AnchorX.Center,
        AnchorY trimY = AnchorY.Center)
    {
        var now = DateTimeOffset.UtcNow;
        return new ImageCopy
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Transform = new ImageTransform(rotation, FlipX: false, FlipY: false),
            ScalingMode = mode,
            TrimmingAnchor = new TrimmingAnchor(trimX, trimY),
            Alignment = new Alignment(alignX, alignY),
            OccupySize = occupy ?? OccupySize.OneByOne,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [Fact]
    public void Cells_Tile_Canvas_Without_Gap_Or_Overlap_When_Evenly_Divisible()
    {
        var canvas = new PixelSize(900, 600);
        const int cols = 3;
        const int rows = 2;

        var rects = new PixelRect[rows, cols];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                rects[y, x] = PlacementGeometry.ComputeDestRect(
                    canvas, cols, rows,
                    new CellPosition(x, y),
                    OccupySize.OneByOne);
            }
        }

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                rects[y, x].Width.Should().Be(300);
                rects[y, x].Height.Should().Be(300);
            }
        }

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols - 1; x++)
            {
                (rects[y, x].X + rects[y, x].Width).Should().Be(rects[y, x + 1].X);
            }
        }
        for (var x = 0; x < cols; x++)
        {
            for (var y = 0; y < rows - 1; y++)
            {
                (rects[y, x].Y + rects[y, x].Height).Should().Be(rects[y + 1, x].Y);
            }
        }

        rects[0, 0].X.Should().Be(0);
        rects[0, 0].Y.Should().Be(0);
        (rects[rows - 1, cols - 1].X + rects[rows - 1, cols - 1].Width).Should().Be(canvas.Width);
        (rects[rows - 1, cols - 1].Y + rects[rows - 1, cols - 1].Height).Should().Be(canvas.Height);
    }

    [Fact]
    public void Cells_Tile_Canvas_Without_Gap_Or_Overlap_When_Not_Evenly_Divisible()
    {
        var canvas = new PixelSize(1000, 1000);
        const int cols = 3;
        const int rows = 3;

        for (var y = 0; y < rows; y++)
        {
            var prevRight = 0;
            for (var x = 0; x < cols; x++)
            {
                var r = PlacementGeometry.ComputeDestRect(
                    canvas, cols, rows,
                    new CellPosition(x, y),
                    OccupySize.OneByOne);
                r.X.Should().Be(prevRight);
                prevRight = r.X + r.Width;
            }
            prevRight.Should().Be(canvas.Width);
        }

        for (var x = 0; x < cols; x++)
        {
            var prevBottom = 0;
            for (var y = 0; y < rows; y++)
            {
                var r = PlacementGeometry.ComputeDestRect(
                    canvas, cols, rows,
                    new CellPosition(x, y),
                    OccupySize.OneByOne);
                r.Y.Should().Be(prevBottom);
                prevBottom = r.Y + r.Height;
            }
            prevBottom.Should().Be(canvas.Height);
        }
    }

    [Fact]
    public void Multi_Cell_Occupy_Equals_Sum_Of_Component_Cells()
    {
        var canvas = new PixelSize(1000, 1000);
        const int cols = 4;
        const int rows = 4;

        var multi = PlacementGeometry.ComputeDestRect(
            canvas, cols, rows,
            new CellPosition(1, 1),
            new OccupySize(2, 3));

        var topLeft = PlacementGeometry.ComputeDestRect(
            canvas, cols, rows,
            new CellPosition(1, 1),
            OccupySize.OneByOne);
        var bottomRight = PlacementGeometry.ComputeDestRect(
            canvas, cols, rows,
            new CellPosition(2, 3),
            OccupySize.OneByOne);

        multi.X.Should().Be(topLeft.X);
        multi.Y.Should().Be(topLeft.Y);
        (multi.X + multi.Width).Should().Be(bottomRight.X + bottomRight.Width);
        (multi.Y + multi.Height).Should().Be(bottomRight.Y + bottomRight.Height);
    }

    [Fact]
    public void Pixel_Offset_Shifts_Origin_But_Preserves_Size()
    {
        var canvas = new PixelSize(800, 600);
        var baseRect = PlacementGeometry.ComputeDestRect(
            canvas, 4, 3,
            new CellPosition(1, 1),
            OccupySize.OneByOne);

        var shifted = PlacementGeometry.ComputeDestRect(
            canvas, 4, 3,
            new CellPosition(1, 1),
            OccupySize.OneByOne,
            pixelOffsetX: 5,
            pixelOffsetY: -3);

        shifted.X.Should().Be(baseRect.X + 5);
        shifted.Y.Should().Be(baseRect.Y - 3);
        shifted.Width.Should().Be(baseRect.Width);
        shifted.Height.Should().Be(baseRect.Height);
    }

    [Fact]
    public void Full_Grid_Occupy_Equals_Whole_Canvas()
    {
        var canvas = new PixelSize(1234, 567);
        var r = PlacementGeometry.ComputeDestRect(
            canvas, 5, 4,
            new CellPosition(0, 0),
            new OccupySize(5, 4));

        r.X.Should().Be(0);
        r.Y.Should().Be(0);
        r.Width.Should().Be(1234);
        r.Height.Should().Be(567);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Throws_When_Grid_Cols_Not_Positive(int cols)
    {
        var canvas = new PixelSize(100, 100);
        var act = () => PlacementGeometry.ComputeDestRect(
            canvas, cols, 1,
            new CellPosition(0, 0),
            OccupySize.OneByOne);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Throws_When_Placement_Exceeds_Grid()
    {
        var canvas = new PixelSize(100, 100);
        var act = () => PlacementGeometry.ComputeDestRect(
            canvas, 3, 3,
            new CellPosition(2, 0),
            new OccupySize(2, 1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ColWeights_2_1_1_Splits_Canvas_Width_In_That_Ratio()
    {
        // 重み 2:1:1、幅 400 → 列幅 200, 100, 100
        var canvas = new PixelSize(400, 100);
        int[] colWeights = [2, 1, 1];

        var c0 = PlacementGeometry.ComputeDestRect(
            canvas, 3, 1, colWeights, null,
            new CellPosition(0, 0), OccupySize.OneByOne);
        var c1 = PlacementGeometry.ComputeDestRect(
            canvas, 3, 1, colWeights, null,
            new CellPosition(1, 0), OccupySize.OneByOne);
        var c2 = PlacementGeometry.ComputeDestRect(
            canvas, 3, 1, colWeights, null,
            new CellPosition(2, 0), OccupySize.OneByOne);

        c0.X.Should().Be(0);
        c0.Width.Should().Be(200);
        c1.X.Should().Be(200);
        c1.Width.Should().Be(100);
        c2.X.Should().Be(300);
        c2.Width.Should().Be(100);
    }

    [Fact]
    public void Weighted_Cells_Tile_Canvas_Without_Gap()
    {
        // 重み 3:2:1 (合計 6) で幅 1200 だと割り切れるが、合計 7 などにすると丸めが発生し得る。
        // ここでは合計 7 で 1200 → 各列 (1200*3/7=514, 1200*5/7=857-514=343, 1200*7/7=1200-857=343) を確認。
        var canvas = new PixelSize(1200, 100);
        int[] colWeights = [3, 2, 2];

        var prevRight = 0;
        for (var x = 0; x < 3; x++)
        {
            var r = PlacementGeometry.ComputeDestRect(
                canvas, 3, 1, colWeights, null,
                new CellPosition(x, 0), OccupySize.OneByOne);
            r.X.Should().Be(prevRight);
            prevRight = r.X + r.Width;
        }
        prevRight.Should().Be(canvas.Width);
    }

    [Fact]
    public void Null_Weights_Falls_Back_To_Uniform()
    {
        // weights=null で旧シグネチャ互換の均等扱い
        var canvas = new PixelSize(300, 100);
        var r = PlacementGeometry.ComputeDestRect(
            canvas, 3, 1, colWeights: null, rowWeights: null,
            new CellPosition(1, 0), OccupySize.OneByOne);
        r.X.Should().Be(100);
        r.Width.Should().Be(100);
    }

    // ----- ComputeRenderedRect のテスト -----

    /// <summary>
    /// UniformContain で正方形画像 100x100、正方形セル 200x200 → アスペクト一致、画像はセル全体に拡大。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_UniformContain_AspectMatch_FillsCell()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformContain);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 100, sourceHeight: 100, copy);

        rect.X.Should().Be(200);
        rect.Y.Should().Be(200);
        rect.Width.Should().Be(200);
        rect.Height.Should().Be(200);
    }

    /// <summary>
    /// UniformContain で縦長画像 (100x200)、正方形セル 200x200 → 高さ一杯、左右に余白。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_UniformContain_TallImage_HasHorizontalPadding()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformContain);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 100, sourceHeight: 200, copy);

        // セル 200x200 内に画像 100x200 (scale=1, drawSize=100x200)、Center 配置
        // 横余白 (200 - 100) / 2 = 50 → 描画矩形 (250, 200, 100, 200)
        rect.X.Should().Be(250);
        rect.Y.Should().Be(200);
        rect.Width.Should().Be(100);
        rect.Height.Should().Be(200);
    }

    /// <summary>
    /// UniformContain で横長画像 (200x100)、正方形セル 200x200 → 幅一杯、上下に余白。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_UniformContain_WideImage_HasVerticalPadding()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformContain);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 200, sourceHeight: 100, copy);

        rect.X.Should().Be(200);
        rect.Y.Should().Be(250);
        rect.Width.Should().Be(200);
        rect.Height.Should().Be(100);
    }

    /// <summary>
    /// UniformCover では余白なし、描画矩形 = セル矩形。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_UniformCover_FillsCell()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformCover);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 100, sourceHeight: 200, copy);

        rect.X.Should().Be(200);
        rect.Y.Should().Be(200);
        rect.Width.Should().Be(200);
        rect.Height.Should().Be(200);
    }

    /// <summary>Fill モードは余白なし、描画矩形 = セル矩形。</summary>
    [Fact]
    public void ComputeRenderedRect_Fill_FillsCell()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.Fill);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(0, 0),
            sourceWidth: 50, sourceHeight: 300, copy);

        rect.Width.Should().Be(200);
        rect.Height.Should().Be(200);
    }

    /// <summary>None（原寸固定）で元寸 < セル寸 → 元寸の小さい矩形が中央配置。</summary>
    [Fact]
    public void ComputeRenderedRect_None_SmallerThanCell_CenteredSmallRect()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.None); // None は TrimmingAnchor で位置決め (Center)
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 80, sourceHeight: 80, copy);

        // セル (200, 200, 200, 200)、画像 80x80 中央 → (260, 260, 80, 80)
        rect.X.Should().Be(260);
        rect.Y.Should().Be(260);
        rect.Width.Should().Be(80);
        rect.Height.Should().Be(80);
    }

    /// <summary>
    /// PixelOffset でセル外に出た部分はクリップされる。Cover + PixelOffset アスペクト一致のケース。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_CoverWithPixelOffset_CreatesGapByClip()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformCover);
        // 100x100 → セル 200x200 で Cover scale=2 → 200x200 = セルにぴったり
        // PixelOffset.X=+50: dst (250, 200, 200, 200)、cellRect (200, 200, 200, 200) → 交差 (250, 200, 150, 200)
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 100, sourceHeight: 100, copy,
            pixelOffsetX: 50, pixelOffsetY: 0);

        rect.X.Should().Be(250);
        rect.Y.Should().Be(200);
        rect.Width.Should().Be(150);
        rect.Height.Should().Be(200);
    }

    /// <summary>
    /// PixelOffset でクリップされて Width=0 になるケース（極端なずれ）。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_OffsetEntirelyOutside_ReturnsEmpty()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.Fill);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 100, sourceHeight: 100, copy,
            pixelOffsetX: 1000, pixelOffsetY: 0);

        (rect.Width == 0 || rect.Height == 0).Should().BeTrue();
    }

    /// <summary>
    /// 回転 90/270 で sw/sh が入れ替わる。横長画像を 90 度回転すると、UniformContain で縦長扱いになる。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_Rotation_Cw90_SwapsAspect()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformContain, rotation: Rotation.Cw90);
        // 元 200x100 (横長) → 回転後 100x200 (縦長) → セル 200x200 で左右余白
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 200, sourceHeight: 100, copy);

        rect.X.Should().Be(250);
        rect.Y.Should().Be(200);
        rect.Width.Should().Be(100);
        rect.Height.Should().Be(200);
    }

    /// <summary>
    /// 占有 N×M の場合は cellRect = 占有範囲全体、画像はその合算矩形にフィット。
    /// </summary>
    [Fact]
    public void ComputeRenderedRect_MultiCellOccupy_UsesUnionRect()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformContain, occupy: new OccupySize(2, 1));
        // 占有 (1,1)-(2,1)、占有合算 cellRect = (200, 200, 400, 200)
        // 画像 100x100 で fitContain = min(4, 2) = 2 → drawSize = 200x200、Center で水平 100 余白
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(1, 1),
            sourceWidth: 100, sourceHeight: 100, copy);

        rect.X.Should().Be(300);
        rect.Y.Should().Be(200);
        rect.Width.Should().Be(200);
        rect.Height.Should().Be(200);
    }

    /// <summary>元画像サイズ 0 は空矩形を返す（ゼロ除算回避）。</summary>
    [Fact]
    public void ComputeRenderedRect_ZeroSourceSize_ReturnsEmpty()
    {
        var canvas = new PixelSize(600, 600);
        var copy = MakeCopy(ScalingMode.UniformContain);
        var rect = PlacementGeometry.ComputeRenderedRect(
            canvas, 3, 3, null, null,
            position: new CellPosition(0, 0),
            sourceWidth: 0, sourceHeight: 100, copy);
        rect.Width.Should().Be(0);
        rect.Height.Should().Be(0);
    }
}
