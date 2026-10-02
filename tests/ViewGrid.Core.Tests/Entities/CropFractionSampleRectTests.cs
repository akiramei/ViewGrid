using FluentAssertions;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Tests.Entities;

/// <summary>
/// <see cref="CropFraction.ToSampleRect"/> (縮小画像向けの切り出し指定) の契約。
/// 低解像度のサムネイルで極細の crop が 0px に丸まり、 画面だけ画像全体を表示してしまう不具合 (R05) の回帰防止。
/// </summary>
public sealed class CropFractionSampleRectTests
{
    // 元解像度 4000x100 の画像の 1px 幅 crop を、 サムネイル 1024x26 に当てはめる。
    private const int SourceWidth = 4000;
    private const int SourceHeight = 100;
    private const int ThumbWidth = 1024;
    private const int ThumbHeight = 26;

    [Theory]
    [InlineData(0)]
    [InlineData(1999)]
    [InlineData(3999)]
    public void OnePixelWideCrop_Is_Valid_At_Source_Resolution_But_Zero_With_ToPixelBbox_On_Thumbnail(int sourceX)
    {
        var crop = new CropFraction((double)sourceX / SourceWidth, 0.0, 1.0 / SourceWidth, 1.0);

        // 元解像度では有効な 1px 幅。
        crop.ToPixelBbox(SourceWidth, SourceHeight).Width.Should().Be(1);
        // サムネイルでは整数 bbox だと 0 に丸まる (= 従来の「crop なし」 扱いに落ちる原因)。
        crop.ToPixelBbox(ThumbWidth, ThumbHeight).Width.Should().Be(0);

        // ToSampleRect は空にせず、 出力は最低 1px。 切り出し元は元画像の同じ位置に対応する分数座標。
        var sample = crop.ToSampleRect(ThumbWidth, ThumbHeight);
        sample.Should().NotBeNull();
        sample!.Value.DstWidth.Should().Be(1);
        sample.Value.DstHeight.Should().Be(ThumbHeight);
        sample.Value.SrcWidth.Should().BeGreaterThan(0).And.BeLessThan(1.0);
        sample.Value.SrcX.Should().BeApproximately((double)sourceX / SourceWidth * ThumbWidth, 1e-9);
        (sample.Value.SrcX + sample.Value.SrcWidth).Should().BeLessThanOrEqualTo(ThumbWidth + 1e-9,
            "右端の 1px crop でもサムネイルの外へはみ出さない");
    }

    [Fact]
    public void Ordinary_Crop_Maps_To_Fractional_Source_And_Rounded_Destination()
    {
        var sample = new CropFraction(0.25, 0.5, 0.5, 0.25).ToSampleRect(200, 100);

        sample.Should().NotBeNull();
        sample!.Value.SrcX.Should().BeApproximately(50, 1e-9);
        sample.Value.SrcY.Should().BeApproximately(50, 1e-9);
        sample.Value.SrcWidth.Should().BeApproximately(100, 1e-9);
        sample.Value.SrcHeight.Should().BeApproximately(25, 1e-9);
        sample.Value.DstWidth.Should().Be(100);
        sample.Value.DstHeight.Should().Be(25);
    }

    [Theory]
    [InlineData(0, 0.0, 0.5, 0.5, 0.5)] // 画像幅 0
    [InlineData(100, 0.0, 0.0, 0.0, 0.5)] // 幅 0 の crop
    [InlineData(100, 0.0, 0.0, 0.5, 0.0)] // 高さ 0 の crop
    [InlineData(100, 1.0, 0.0, 0.5, 0.5)] // 画像の外側 (X が右端)
    public void Degenerate_Or_Outside_Crop_Returns_Null(int width, double x, double y, double w, double h)
    {
        new CropFraction(x, y, w, h).ToSampleRect(width, 100).Should().BeNull();
    }
}
