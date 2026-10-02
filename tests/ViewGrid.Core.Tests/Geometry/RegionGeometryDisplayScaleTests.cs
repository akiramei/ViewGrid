using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// 親画像の cell 内描画サイズ (<see cref="RegionGeometry.ComputeParentDrawSize"/>) と
/// crop 後・回転後の寸法 (<see cref="RegionGeometry.ComputeTransformedCropSize"/>) の契約。
/// 画面表示 (GridCanvasView) と出力 (renderer) が同じ倍率を使うことを固定する (R04 / R06)。
/// </summary>
public sealed class RegionGeometryDisplayScaleTests
{
    private static readonly ImageTransform NoTransform = ImageTransform.Identity;
    private static readonly ImageTransform Rotate90 = new(Rotation.Cw90, FlipX: false, FlipY: false);

    // ─── R04: 非正方形 crop + 回転 ───────────────────────────────

    [Fact]
    public void TransformedCropSize_Fixes_Integer_Crop_On_Source_Then_Swaps_Axes_For_Rotation()
    {
        // 400x200 を幅 0.25 / 高さ 1 で crop → 元画像座標で 100x200。 90° 回転後は 200x100。
        var crop = new CropFraction(0.0, 0.0, 0.25, 1.0);

        RegionGeometry.ComputeTransformedCropSize(crop, Rotate90, 400, 200)
            .Should().Be((200, 100));
        RegionGeometry.ComputeTransformedCropSize(crop, NoTransform, 400, 200)
            .Should().Be((100, 200));
    }

    [Fact]
    public void ParentDrawSize_NonSquareCrop_Rotated_Contain_Uses_200x100_Not_50x400()
    {
        // 報告の再現条件: 400x200 画像を幅 0.25・高さ 1 で crop して 90° 回転し、 200x200 セルに contain。
        // 正しい変換後寸法は 200x100 (旧: 先に寸法だけ入れ替えて crop 比率を掛け、 50x400 になっていた)。
        var crop = new CropFraction(0.0, 0.0, 0.25, 1.0);

        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            200, 200, Rotate90, ScalingMode.UniformContain, crop, 400, 200);

        w.Should().BeApproximately(200, 1e-9);
        h.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void RegionAssetScale_Matches_Output_For_NonSquareCrop_Rotated_Parent()
    {
        // 保護領域 (元画像で 40x20 px) は親と同じ source→cell 倍率で描かれる。 倍率は 1.0 (200x100 を 200x100 に
        // 描くため) で、 画面でも 40x20 のまま (旧: 0.5 倍の 20x10 になっていた)。
        var crop = new CropFraction(0.0, 0.0, 0.25, 1.0);
        var (dispW, dispH) = RegionGeometry.ComputeParentDrawSize(
            200, 200, Rotate90, ScalingMode.UniformContain, crop, 400, 200);
        var (tw, th) = RegionGeometry.ComputeTransformedCropSize(crop, Rotate90, 400, 200);

        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(Rotate90, tw, th, dispW, dispH);

        (40 * sx).Should().BeApproximately(40, 1e-9);
        (20 * sy).Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void TransformedCropSize_Without_Crop_Is_Whole_Image_And_Swaps_On_Rotation()
    {
        RegionGeometry.ComputeTransformedCropSize(null, NoTransform, 400, 200).Should().Be((400, 200));
        RegionGeometry.ComputeTransformedCropSize(null, Rotate90, 400, 200).Should().Be((200, 400));
    }

    // ─── R06: 縮小のみ / 拡大のみ は元画像ピクセル基準 ───────────────

    [Fact]
    public void ParentDrawSize_ShrinkOnly_400px_Image_In_1000px_Cell_Stays_400()
    {
        // 400x400 を 1000x1000 のセルに縮小のみで置くと、 出力は等倍の 400x400 (拡大しない)。
        // 600 DIP 表示 (倍率 0.6) ではこれが 240 DIP。 サムネの DIP 寸法で判定すると 400 DIP になっていた。
        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            1000, 1000, NoTransform, ScalingMode.UniformContainShrinkOnly, null, 400, 400);

        w.Should().BeApproximately(400, 1e-9);
        h.Should().BeApproximately(400, 1e-9);
        (w * 0.6).Should().BeApproximately(240, 1e-9);
    }

    [Fact]
    public void ParentDrawSize_ShrinkOnly_Larger_Image_Shrinks_To_Fit()
    {
        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            200, 200, NoTransform, ScalingMode.UniformContainShrinkOnly, null, 400, 400);

        w.Should().BeApproximately(200, 1e-9);
        h.Should().BeApproximately(200, 1e-9);
    }

    [Fact]
    public void ParentDrawSize_EnlargeOnly_Smaller_Cell_Keeps_Source_Size_And_Overflows()
    {
        // 拡大のみ: 元画像 400x400 を 200x200 セルに置いても縮小せず 400x400 (セルからはみ出し、 表示側でクリップ)。
        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            200, 200, NoTransform, ScalingMode.UniformContainEnlargeOnly, null, 400, 400);

        w.Should().BeApproximately(400, 1e-9);
        h.Should().BeApproximately(400, 1e-9);
    }

    [Fact]
    public void ParentDrawSize_EnlargeOnly_Larger_Cell_Enlarges_To_Fit()
    {
        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            800, 800, NoTransform, ScalingMode.UniformContainEnlargeOnly, null, 400, 400);

        w.Should().BeApproximately(800, 1e-9);
        h.Should().BeApproximately(800, 1e-9);
    }

    [Fact]
    public void ParentDrawSize_None_Is_Source_Pixels_After_Crop()
    {
        var crop = new CropFraction(0.0, 0.0, 0.5, 1.0);

        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            1000, 1000, NoTransform, ScalingMode.None, crop, 400, 200);

        w.Should().BeApproximately(200, 1e-9);
        h.Should().BeApproximately(200, 1e-9);
    }

    [Fact]
    public void ParentDrawSize_Fill_Is_Cell_Size()
    {
        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            300, 120, NoTransform, ScalingMode.Fill, null, 400, 200);

        w.Should().BeApproximately(300, 1e-9);
        h.Should().BeApproximately(120, 1e-9);
    }

    [Theory]
    [InlineData(0, 100, 400, 400)]
    [InlineData(100, 0, 400, 400)]
    [InlineData(100, 100, 0, 400)]
    [InlineData(100, 100, 400, 0)]
    public void ParentDrawSize_Degenerate_Input_Returns_Zero(double cellW, double cellH, int sourceW, int sourceH)
    {
        RegionGeometry.ComputeParentDrawSize(
                cellW, cellH, NoTransform, ScalingMode.UniformContain, null, sourceW, sourceH)
            .Should().Be((0.0, 0.0));
    }
}
