using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// 画面表示がサムネイルの縦横比に頼ってよいか (<see cref="RegionGeometry.ThumbnailAspectDeviatesFromCrop"/>)。
/// 極細の crop ではサムネが整数画素に丸められて比率が崩れ、 画面だけ出力と違う太さになる (R09)。
/// </summary>
public sealed class ThumbnailAspectDeviationTests
{
    private static readonly ImageTransform None = ImageTransform.Identity;

    [Fact]
    public void One_Pixel_Wide_Crop_Of_A_Wide_Image_Deviates_From_Its_One_By_Twenty_Six_Thumbnail()
    {
        // 4000x100 の画像の中央 1px 幅 (高さ全体)。 出力は 1x100 (比率 1:100)。
        // サムネ (長辺 1024) では幅 0.256px → 1px に切り上げ、 高さ 25.6 → 26px になり、 比率は 1:26。
        var crop = new CropFraction(0.5, 0.0, 1.0 / 4000, 1.0);

        RegionGeometry.ThumbnailAspectDeviatesFromCrop(crop, None, 4000, 100, thumbWidth: 1, thumbHeight: 26)
            .Should().BeTrue();
    }

    [Fact]
    public void A_Proportional_Crop_Does_Not_Deviate()
    {
        // 4000x1000 の左半分 (2000x1000, 2:1)。 サムネ 1024x256 の左半分 512x128 (4:1)... ではなく 512x256 (2:1)。
        var crop = new CropFraction(0.0, 0.0, 0.5, 1.0);

        RegionGeometry.ThumbnailAspectDeviatesFromCrop(crop, None, 4000, 1000, thumbWidth: 512, thumbHeight: 256)
            .Should().BeFalse();
    }

    [Fact]
    public void Rotation_Is_Taken_Into_Account_When_Comparing_Aspects()
    {
        // 幅 400 / 高さ 200 の画像の幅 100 分 (100x200)。 90° 回転後は 200x100 (2:1)。 焼き込み済みサムネも 2:1。
        var crop = new CropFraction(0.0, 0.0, 0.25, 1.0);
        var rotate = new ImageTransform(Rotation.Cw90, FlipX: false, FlipY: false);

        RegionGeometry.ThumbnailAspectDeviatesFromCrop(crop, rotate, 400, 200, thumbWidth: 200, thumbHeight: 100)
            .Should().BeFalse();
        RegionGeometry.ThumbnailAspectDeviatesFromCrop(crop, rotate, 400, 200, thumbWidth: 100, thumbHeight: 200)
            .Should().BeTrue("回転を考慮しない向きのサムネは比率が食い違う");
    }

    [Fact]
    public void No_Crop_Never_Deviates_And_Invalid_Sizes_Are_Ignored()
    {
        RegionGeometry.ThumbnailAspectDeviatesFromCrop(null, None, 100, 100, 1, 26).Should().BeFalse();
        RegionGeometry.ThumbnailAspectDeviatesFromCrop(new CropFraction(0, 0, 0.5, 1), None, 0, 100, 10, 10)
            .Should().BeFalse();
        RegionGeometry.ThumbnailAspectDeviatesFromCrop(new CropFraction(0, 0, 0.5, 1), None, 100, 100, 0, 10)
            .Should().BeFalse();
    }

    [Fact]
    public void Display_Draw_Size_Matches_The_Output_For_The_One_Pixel_Crop()
    {
        // 画面が使う描画サイズ (ComputeParentDrawSize) は、 出力と同じく 1x100 を 200x200 の cell に収めた 2x200。
        var crop = new CropFraction(0.5, 0.0, 1.0 / 4000, 1.0);

        var (w, h) = RegionGeometry.ComputeParentDrawSize(
            200, 200, None, ScalingMode.UniformContain, crop, 4000, 100);

        w.Should().BeApproximately(2.0, 1e-9);
        h.Should().BeApproximately(200.0, 1e-9);
    }
}
